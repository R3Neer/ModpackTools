using System.Collections.Immutable;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Build;

internal sealed record ExpectedManagedArtifact(string Path, string LocalPath, ManagedDownload Download, string Side, bool Optional, ProviderFile? Provider);
internal sealed record BuildArtifactExpectation(string Name, string Version, ImmutableDictionary<string, string> Dependencies,
    ImmutableDictionary<string, string> PlainFiles, ImmutableArray<ExpectedManagedArtifact> Managed)
{
    public static BuildArtifactExpectation Read(ProjectContext stage, ProjectIndexReport index, ProjectHealthObservation health, CancellationToken cancellation)
        => Read(stage, index, health.Inventory, health.Nodes.Where(node => node.Item.MetadataPath is not null).ToDictionary(node => node.Item.MetadataPath!, node => node.File, StringComparer.OrdinalIgnoreCase), cancellation);
    public static BuildArtifactExpectation Read(ProjectContext stage, ProjectIndexReport index, RawInventorySnapshot inventory, CancellationToken cancellation)
        => Read(stage, index, inventory, inventory.Mods.Concat(inventory.Resources).Concat(inventory.Shaders).Where(item => item.MetadataPath is not null)
            .ToDictionary(item => item.MetadataPath!, _ => (ProviderFile?)null, StringComparer.OrdinalIgnoreCase), cancellation);
    private static BuildArtifactExpectation Read(ProjectContext stage, ProjectIndexReport index, RawInventorySnapshot inventory,
        IReadOnlyDictionary<string, ProviderFile?> providers, CancellationToken cancellation)
    {
        if (!index.Valid) throw new ArgumentException("Build expectations require a verified index.", nameof(index));
        var manifest = TomlDeclarationReader.Read(TextFile.Read(TransactionFiles.Within(stage.Root, "pack.toml")), new());
        if (manifest.Get("versions") is not TomlFields versions) throw Invalid("Pack versions are missing");
        var loaders = new[] { "fabric", "quilt", "forge", "neoforge" }.Where(loader => versions.Get(loader) is not TomlMissing).ToArray();
        if (loaders.Length != 1) throw Invalid("A build must declare exactly one supported mod loader");
        var dependencies = ImmutableDictionary<string, string>.Empty.Add("minecraft", Required(versions.Get("minecraft")));
        dependencies = dependencies.Add(loaders[0] switch { "fabric" => "fabric-loader", "quilt" => "quilt-loader", var value => value }, Required(versions.Get(loaders[0])));
        var indexRoot = Path.GetDirectoryName(stage.IndexPath)!; var plain = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        var managed = ImmutableArray.CreateBuilder<ExpectedManagedArtifact>(); var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in index.Files)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!file.Metadata) { plain.Add(file.RelativePath.Replace('\\', '/'), file.Sha256); continue; }
            if (!providers.ContainsKey(file.Path)) throw Invalid("Indexed metadata has no health observation: " + file.RelativePath);
            var document = TomlDeclarationReader.Read(TextFile.Read(file.Path), new());
            var local = TransactionFiles.Within(Path.GetDirectoryName(file.Path)!, Required(document.Get("filename")));
            var relative = Path.GetRelativePath(indexRoot, local).Replace('\\', '/'); _ = TransactionFiles.Within(indexRoot, relative);
            if (!destinations.Add(relative)) throw Invalid("Managed artifacts share a destination: " + relative);
            var facts = ManagedInstallFactsReader.Read(document); var download = facts.Download ?? throw Invalid("Managed artifact has no declared download: " + relative);
            var algorithm = download.Algorithm.ToLowerInvariant(); var length = algorithm switch { "sha1" => 40, "sha256" => 64, "sha512" => 128, "md5" => 32, _ => 0 };
            if (length == 0 || download.Hash.Length != length || download.Hash.Any(value => !Uri.IsHexDigit(value))) throw Invalid("Managed artifact hash is invalid: " + relative);
            var side = Text(document.Get("side")).ToLowerInvariant(); if (side.Length == 0) side = "both";
            if (side is not ("client" or "server" or "both")) throw Invalid("Managed artifact side is invalid: " + relative);
            managed.Add(new(relative, local, download with { Algorithm = algorithm }, side, facts.Optional, providers.GetValueOrDefault(file.Path)));
        }
        foreach (var destination in destinations)
            if (plain.ContainsKey(destination)) throw Invalid("Plain and managed artifacts share a destination: " + destination);
        foreach (var item in inventory.Mods.Concat(inventory.Resources).Concat(inventory.Shaders).Where(item => item.Source == ContentSource.Local))
        {
            cancellation.ThrowIfCancellationRequested();
            var folder = item.Kind switch { ContentKind.Mod => "mods", ContentKind.ResourcePack => "resourcepacks", _ => "shaderpacks" };
            var full = TransactionFiles.Within(stage.Root, folder + "/" + item.Filename); var relative = Path.GetRelativePath(indexRoot, full).Replace('\\', '/');
            if (!plain.ContainsKey(relative)) throw Invalid("Local content is missing from the index: " + relative);
        }
        return new(Required(manifest.Get("name")), Required(manifest.Get("version")), dependencies, plain.ToImmutable(), managed.ToImmutable());
    }
    private static string Text(TomlDeclarationValue value) => value is TomlText text ? text.Value : "";
    private static string Required(TomlDeclarationValue value) => Text(value) is { Length: > 0 } text ? text : throw Invalid("A required build declaration is missing or is not a string");
    private static DomainException Invalid(string message) => new("Build.InvalidExpectation", message, "repair the project or its Packwiz metadata before building", "InvalidData");
}
