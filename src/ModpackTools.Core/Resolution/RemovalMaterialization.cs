using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

// Only the enclosing transaction owns this disposable stage. Validate every
// ownership boundary and prospective document before deleting its first file.
internal static class RemovalMaterialization
{
    public static void Apply(ProjectContext stage, RemovalPlan plan, long maximumArtifactBytes, CancellationToken cancellation)
    {
        var metadata = EditorialMetadataReader.Read(stage);
        var protectedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in plan.Nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            var item = node.Item; var folder = Folder(stage, item.Kind);
            if (item.MetadataPath is { } path)
            {
                path = TransactionFiles.Within(folder, Path.GetRelativePath(folder, path));
                protectedPaths[path] = item.Name; folder = Path.GetDirectoryName(path)!;
            }
            protectedPaths[TransactionFiles.Within(folder, Filename(item))] = item.Name;
        }
        var deletions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var resourceIds = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var mods = metadata.Mods.Entries; var content = metadata.Content?.Entries; var resources = metadata.ResourcePacks.Entries;
        foreach (var change in plan.Changes)
        {
            cancellation.ThrowIfCancellationRequested(); var item = change.Before.Item;
            var folder = Folder(stage, item.Kind); string target;
            if (item.Source == ContentSource.Packwiz)
            {
                if (item.MetadataPath is not { } path) throw Invalid("Managed removal requires physical metadata", item.Id);
                target = TransactionFiles.Within(folder, Path.GetRelativePath(folder, path));
                var artifact = TransactionFiles.Within(Path.GetDirectoryName(target)!, Filename(item));
                Protected(artifact, $"Artifact '{item.Filename}' is shared with retained content");
                if (File.Exists(artifact))
                {
                    var download = ManagedInstallFactsReader.Read(TomlDeclarationReader.Read(TextFile.Read(target), new())).Download;
                    if (download is null || !Matches(artifact, download, maximumArtifactBytes, hashes, cancellation))
                        throw new DomainException("Remove.ArtifactMismatch", $"Local artifact '{item.Filename}' differs from Packwiz metadata",
                            "preserve or reconcile the local file before removing this item", "InvalidData");
                    deletions.Add(artifact);
                }
            }
            else if (item.Source == ContentSource.Local) target = TransactionFiles.Within(folder, Filename(item));
            else throw Invalid("Removal source is not a physical file", item.Id);
            Protected(target, "Removal path is owned by retained content");
            if (!File.Exists(target)) throw new DomainException("Content.NotFound", $"Removal target '{item.Name}' is missing", "refresh content list and retry", "ObjectNotFound");
            deletions.Add(target); mods = mods.Remove(item.Id); content = content?.Remove(item.Id);
            if (item.Kind == ContentKind.ResourcePack)
            {
                var defaultId = "file/" + item.Filename; resourceIds.Add(defaultId);
                resources = resources.Remove(defaultId).Remove(item.Id);
            }
        }
        var active = ResourcePackOrderReader.Read(stage);
        var ordered = active.Where(id => !resourceIds.Contains(id)).ToArray();
        string? editorial = null;
        if (!plan.Changes.IsEmpty)
        {
            var entries = metadata.Data.Entries.SetItem("Mods", new PsdTable(mods)).SetItem("ResourcePacks", new PsdTable(resources));
            if (content is not null) entries = entries.SetItem("Content", new PsdTable(content));
            editorial = PsdDataWriter.Write(new(entries), 4 * 1024 * 1024);
            _ = PsdReader.Parse(editorial);
        }
        cancellation.ThrowIfCancellationRequested();
        ResourceOrderEditor.Write(stage, ordered, active);
        foreach (var path in deletions)
        {
            cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(path); File.Delete(path);
        }
        if (editorial is not null)
        {
            var path = TransactionFiles.Within(stage.Root, ".modpack/metadata.psd1");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, editorial, new UTF8Encoding(false, true));
            _ = EditorialMetadataReader.Read(stage);
        }
        void Protected(string path, string message)
        {
            if (protectedPaths.TryGetValue(path, out var owner)) throw new DomainException("Remove.SharedArtifact", message,
                "reconcile shared file ownership before removal", "InvalidData", details: owner);
        }
    }
    private static string Folder(ProjectContext stage, ContentKind kind) => TransactionFiles.Within(stage.Root, kind switch
    { ContentKind.Mod => "mods", ContentKind.ResourcePack => "resourcepacks", ContentKind.ShaderPack => "shaderpacks", _ => throw Invalid("Removal kind is invalid", null) });
    private static string Filename(InventoryItem item) => !string.IsNullOrEmpty(item.Filename) ? item.Filename : throw Invalid("Removal filename is missing", item.Id);
    private static DomainException Invalid(string message, string? id) => new("Remove.InvalidState", message, "observe physical content again", "InvalidData", id);
    private static bool Matches(string path, ManagedDownload download, long maximumBytes,
        Dictionary<string, Dictionary<string, string>> hashes, CancellationToken cancellation)
    {
        var algorithm = download.Algorithm.ToLowerInvariant() switch
        { "sha1" => HashAlgorithmName.SHA1, "sha256" => HashAlgorithmName.SHA256, "sha512" => HashAlgorithmName.SHA512, "md5" => HashAlgorithmName.MD5, _ => default };
        if (algorithm.Name is null) return false;
        if (!hashes.TryGetValue(path, out var algorithms)) hashes.Add(path, algorithms = new(StringComparer.Ordinal));
        if (algorithms.TryGetValue(algorithm.Name, out var cached)) return StringComparer.OrdinalIgnoreCase.Equals(download.Hash, cached);
        WindowsProjectIdentity.GuardAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, maximumBytes, path);
        using var hash = IncrementalHash.CreateHash(algorithm); var buffer = ArrayPool<byte>.Shared.Rent(65536); long total = 0;
        try
        {
            int count;
            while ((count = input.Read(buffer, 0, 65536)) != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (count > maximumBytes - total) throw new DomainException("Remove.ArtifactLimitExceeded", "Removal artifact exceeds its byte quota", "use an explicit bounded artifact quota", "LimitsExceeded", path);
                total += count; hash.AppendData(buffer, 0, count);
            }
            cancellation.ThrowIfCancellationRequested(); var actual = Convert.ToHexString(hash.GetHashAndReset()); algorithms.Add(algorithm.Name, actual);
            return StringComparer.OrdinalIgnoreCase.Equals(download.Hash, actual);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
