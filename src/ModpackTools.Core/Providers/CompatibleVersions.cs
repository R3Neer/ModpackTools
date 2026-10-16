using System.Globalization;
using System.Collections.Immutable;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Providers;

public static class CompatibleVersions
{
    public static Task<CompatibleVersionView> ReadForSelectorAsync(ProjectContext project, string selector,
        string configurationDirectory, DateTimeOffset now, ProviderRequestLimits? requests = null,
        ProviderModelLimits? models = null, ReferenceCacheWriteLimits? cache = null,
        CancellationToken cancellation = default, HttpMessageHandler? handler = null, Uri? baseUri = null)
    {
        var reference = ReferenceCacheResolver.ResolveSelectorFromDirectory(configurationDirectory, selector, project.Id,
            now, new(["mod", "resourcepack", "shaderpack"], true));
        var snapshot = InventoryReader.Read(project);
        var resolved = reference ?? selector;
        if (resolved.Length == 0) throw new DomainException("Content.NotFound", $"Content '{resolved}' was not found in project '{project.Id}'",
            "modpack content list", "ObjectNotFound", resolved);
        var item = UpdateTargetSelector.Resolve(snapshot, [resolved])[0];
        return ReadAsync(project, item, configurationDirectory, now, requests, models, cache, cancellation, handler, baseUri);
    }

    public static async Task<CompatibleVersionView> ReadAsync(ProjectContext project, InventoryItem item,
        string configurationDirectory, DateTimeOffset now, ProviderRequestLimits? requests = null,
        ProviderModelLimits? models = null, ReferenceCacheWriteLimits? cache = null,
        CancellationToken cancellation = default, HttpMessageHandler? handler = null, Uri? baseUri = null)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(item);
        if (!item.Id.StartsWith("modrinth:", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Versions.UnsupportedProvider", $"Content '{item.Name}' does not use Modrinth version metadata",
                "version selection currently requires Modrinth-managed content", "NotImplemented", item.Id);
        var maximumBytes = (cache ?? new()).MaximumBytes;
        if (maximumBytes <= 0) throw new DomainException("Versions.InvalidCacheWritePolicy", "Version cache byte quota must be positive",
            "use a positive version cache byte quota", target: nameof(ReferenceCacheWriteLimits.MaximumBytes));
        try
        {
            await using var provider = new ModrinthVersionClient(configurationDirectory, now, requests, cancellation, handler, baseUri, models);
            var candidates = await provider.GetCompatibleVersionsAsync(item.Id[9..], item.Kind, project.Minecraft ?? "", project.Loader ?? "").ConfigureAwait(false);
            if (provider.Diagnostics.FirstOrDefault() is { } cleanup)
                throw new DomainException("Metadata.ProviderCleanupFailed", "Provider response temporary file could not be removed",
                    "check private cache permissions and file locks", "WriteError", cleanup.Path, details: cleanup.Message);
            var rows = candidates.Select((value, index) => new CompatibleVersionRow(index + 1, value,
                string.Equals(value.Version.Id, item.VersionId, StringComparison.OrdinalIgnoreCase))).ToImmutableArray();
            var view = new CompatibleVersionView(project.Id, project.Minecraft, project.Loader, item.Id, item.Name, item.Kind, item.VersionId, rows);
            cancellation.ThrowIfCancellationRequested();
            Write(configurationDirectory, view, now, maximumBytes, cancellation);
            return view;
        }
        catch (OperationCanceledException)
        { throw new DomainException("Core.OperationCancelled", "Version listing was cancelled", "retry when ready", "OperationStopped"); }
    }

    private static void Write(string configurationDirectory, CompatibleVersionView view, DateTimeOffset now,
        long maximumBytes, CancellationToken cancellation)
    {
        var directory = Path.GetFullPath(configurationDirectory); var path = Path.Combine(directory, "last-versions.json");
        void Guard()
        {
            cancellation.ThrowIfCancellationRequested();
            for (FileSystemInfo? entry = new FileInfo(path); entry is not null;
                entry = entry is DirectoryInfo parent ? parent.Parent : ((FileInfo)entry).Directory)
                if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new DomainException("Versions.LinkedCachePath",
                    "Version cache path is linked", "use a regular private cache directory", "InvalidData", entry.FullName);
        }
        Guard();
        AtomicJsonCacheFile.Write(directory, path, maximumBytes, writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("SchemaVersion", 1);
            writer.WriteString("CreatedUtc", now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("ProjectId", view.ProjectId); writer.WriteString("MinecraftVersion", view.MinecraftVersion);
            writer.WriteString("Loader", view.Loader); writer.WriteString("ItemId", view.ItemId); writer.WriteString("ItemName", view.ItemName);
            writer.WriteString("ItemKind", Kind(view.ItemKind)); writer.WriteString("InstalledVersionId", view.InstalledVersionId);
            writer.WriteStartArray("Versions");
            foreach (var row in view.Versions)
            {
                cancellation.ThrowIfCancellationRequested();
                var candidate = row.Candidate; var version = candidate.Version;
                writer.WriteStartObject(); writer.WriteNumber("Index", row.Number); writer.WriteString("Id", version.Id);
                writer.WriteString("ProjectId", version.ProjectId); writer.WriteString("Name", version.Name);
                writer.WriteString("VersionNumber", version.VersionNumber); writer.WriteString("VersionType", candidate.VersionType);
                writer.WriteString("Published", version.Published.Text); writer.WriteString("Filename", candidate.Filename);
                writer.WriteStartArray("Loaders"); foreach (var loader in version.Loaders) writer.WriteStringValue(loader); writer.WriteEndArray();
                writer.WriteStartArray("GameVersions"); foreach (var game in version.GameVersions) writer.WriteStringValue(game); writer.WriteEndArray();
                writer.WritePropertyName("Dependencies"); writer.WriteRawValue(candidate.DependencyCacheJson);
                writer.WriteBoolean("Installed", row.Installed); writer.WriteEndObject(); writer.Flush();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }, () => new DomainException("Versions.CacheWriteLimit", "Version cache exceeds its serialized byte quota",
            "use a bounded version listing", "LimitsExceeded", path),
            details => new DomainException("Versions.CacheWriteFailed", "Version cache could not be updated",
                "check configuration permissions and file locks", "WriteError", path, details: details),
            Guard, new() { MaxDepth = 256 });
    }

    internal static string Kind(ContentKind kind) => kind switch
    { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", ContentKind.ShaderPack => "shaderpack", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
}
