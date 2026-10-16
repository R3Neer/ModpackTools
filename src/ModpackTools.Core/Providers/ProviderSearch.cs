using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Providers;

public sealed record SearchHit(int Number, string ProjectId, string Slug, string Type, string Title,
    string Author, string Description, long Downloads);
public sealed record SearchResults(long TotalHits, ImmutableArray<SearchHit> Items);
public sealed record SearchView(string Query, string Type, ProjectContext? Project, SearchResults Results);

public static class ProviderSearch
{
    public static string NormalizeType(string type) => InventorySelector.NormalizeType(type) switch
    { InventoryType.Mod => "mod", InventoryType.ResourcePack => "resourcepack", InventoryType.ShaderPack => "shaderpack", _ => "all" };

    internal static string Endpoint(string query, string type, int limit, ProjectContext? project)
    {
        if (query.Length > 16384 || project?.Minecraft?.Length > 256 || project?.Loader?.Length > 256)
            throw new DomainException("Metadata.ProviderRequestLimit", "Search query or compatibility text exceeds its limit",
                "use a query of at most16384 characters and compatibility values of at most256 characters", "LimitsExceeded", "MaximumSearchQueryCharacters");
        var groups = new List<string[]>();
        if (project is not null) groups.Add(["versions:" + project.Minecraft]);
        var apiType = type == "shaderpack" ? "shader" : type;
        groups.Add(apiType == "all" ? ["project_type:mod", "project_type:resourcepack", "project_type:shader"] : ["project_type:" + apiType]);
        if (project is not null && apiType == "mod" && !string.IsNullOrEmpty(project.Loader))
            groups.Add(["categories:" + project.Loader.ToLowerInvariant()]);
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var group in groups) { writer.WriteStartArray(); foreach (var facet in group) writer.WriteStringValue(facet); writer.WriteEndArray(); }
            writer.WriteEndArray();
        }
        return "search?query=" + Uri.EscapeDataString(query) + "&facets=" + Uri.EscapeDataString(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length))
            + "&index=relevance&limit=" + limit.ToString(CultureInfo.InvariantCulture);
    }

    internal static SearchResults Decode(JsonElement root, CacheDateTable dates, int maximumHits, Action entry)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Search response must be an object.");
        var hits = Field(root, "hits");
        if (hits.ValueKind != JsonValueKind.Array) throw new FormatException("Search hits must be an array.");
        if (hits.GetArrayLength() > maximumHits) throw new DomainException("Metadata.ProviderRequestLimit", "Search result count exceeds its limit",
            "use bounded search results", "LimitsExceeded", "MaximumSearchResults");
        var total = Number(Field(root, "total_hits"));
        var results = ImmutableArray.CreateBuilder<SearchHit>(hits.GetArrayLength());
        foreach (var hit in hits.EnumerateArray())
        {
            entry();
            var kind = Text(hit, "project_type");
            results.Add(new(results.Count + 1, Text(hit, "project_id"), Text(hit, "slug"), kind.Equals("shader", StringComparison.InvariantCultureIgnoreCase) ? "shaderpack" : kind,
                Text(hit, "title"), Text(hit, "author"), Text(hit, "description"), Number(Field(hit, "downloads"))));
        }
        return new(total, results.ToImmutable());

        string Text(JsonElement value, string name)
        {
            var scalar = Field(value, name);
            if (scalar.ValueKind is JsonValueKind.Array or JsonValueKind.Object) throw new FormatException("Search text field '" + name + "' must be scalar.");
            return CacheJson.String(new(scalar), dates);
        }
        static long Number(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null => 0,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.Number => checked((long)decimal.Round(value.GetDecimal(), 0, MidpointRounding.ToEven)),
            JsonValueKind.String when string.IsNullOrWhiteSpace(value.GetString()) => 0,
            JsonValueKind.String => long.Parse(value.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture),
            _ => throw new FormatException("Search count must be a signed 64-bit integer.")
        };
    }

    private static JsonElement Field(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Search hit must be an object.");
        foreach (var (key, item) in JsonDisplayProjection.Properties(value)) if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) return item;
        throw new FormatException("Search response is missing field '" + name + "'.");
    }

    public static async Task<SearchView> ReadAsync(string query, string type, int limit, ProjectContext? project,
        string configurationDirectory, DateTimeOffset now, ProviderRequestLimits? requests = null,
        ReferenceCacheWriteLimits? cache = null, CancellationToken cancellation = default, HttpMessageHandler? handler = null, Uri? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new DomainException("Search.EmptyQuery", "The search query cannot be empty", "modpack content search <query>");
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));
        type = NormalizeType(type);
        var maximumBytes = (cache ?? new()).MaximumBytes;
        if (maximumBytes <= 0) throw new DomainException("Search.InvalidCacheWritePolicy", "Search cache byte quota must be positive", "use a positive cache quota", target: "MaximumBytes");
        await using var provider = new ModrinthVersionClient(configurationDirectory, now, requests, cancellation, handler, baseUri);
        var results = await provider.SearchAsync(query, type, limit, project).ConfigureAwait(false);
        if (provider.Diagnostics.FirstOrDefault() is { } cleanup)
            throw new DomainException("Metadata.ProviderCleanupFailed", "Provider response temporary file could not be removed", "check private cache permissions and file locks", "WriteError", cleanup.Path, details: cleanup.Message);
        var view = new SearchView(query, type, project, results);
        var directory = Path.GetFullPath(configurationDirectory); var path = Path.Combine(directory, "last-search.json");
        void Guard()
        {
            if (cancellation.IsCancellationRequested) throw new DomainException("Core.OperationCancelled", "Search was cancelled", "retry when ready", "OperationStopped");
            for (FileSystemInfo? info = new FileInfo(path); info is not null; info = info is DirectoryInfo parent ? parent.Parent : ((FileInfo)info).Directory)
                if (!string.IsNullOrEmpty(info.LinkTarget)) throw new DomainException("Search.LinkedCachePath", "Search cache path is linked", "use a regular private cache directory", "InvalidData", info.FullName);
        }
        Guard();
        AtomicJsonCacheFile.Write(directory, path, maximumBytes, writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("SchemaVersion", 1); writer.WriteString("CreatedUtc", now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("Query", query); writer.WriteString("ProjectId", project?.Id); writer.WriteString("MinecraftVersion", project?.Minecraft); writer.WriteString("Loader", project?.Loader);
            writer.WriteString("Type", type); writer.WriteNumber("TotalHits", results.TotalHits); writer.WriteStartArray("Results");
            foreach (var hit in results.Items)
            {
                if (cancellation.IsCancellationRequested) throw new DomainException("Core.OperationCancelled", "Search was cancelled", "retry when ready", "OperationStopped");
                writer.WriteStartObject(); writer.WriteNumber("Index", hit.Number); writer.WriteString("ProjectId", hit.ProjectId); writer.WriteString("Slug", hit.Slug);
                writer.WriteString("Type", hit.Type); writer.WriteString("Title", hit.Title); writer.WriteString("Author", hit.Author); writer.WriteString("Description", hit.Description);
                writer.WriteNumber("Downloads", hit.Downloads); writer.WriteEndObject(); writer.Flush();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }, () => new DomainException("Search.CacheWriteLimit", "Search cache exceeds its serialized byte quota", "use a smaller result limit", "LimitsExceeded", path),
            details => new DomainException("Search.CacheWriteFailed", "Search cache could not be updated", "check configuration permissions and file locks", "WriteError", path, details: details), Guard);
        return view;
    }
}
