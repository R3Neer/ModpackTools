using System.Text.Json;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public sealed class CachedCategorySnapshot
{
    private readonly Lazy<CacheRows> categories;
    private readonly Lazy<string> project;
    private readonly Lazy<JsonDisplayValue> created;
    internal CachedCategorySnapshot(OwnedCacheData data, string path, ReferenceCacheReadLimits limits)
    {
        Data = data.Data;
        Dates = data.Dates;
        Path = path;
        categories = new(() => Access(() =>
        {
            var rows = new CacheRows(CacheFields.Field(Data, "Categories"), Path, Dates);
            if (rows.Count > limits.MaximumRows) throw CategoryCacheReader.Limit(Path, "Category occurrence count exceeds the reader quota.");
            return rows;
        }));
        project = new(() => Access(() => CacheJson.String(CacheFields.Field(Data, "ProjectId"), Dates)));
        created = new(() => Access(() => CacheFields.Field(Data, "CreatedUtc")));
    }
    public JsonElement Data { get; }
    public string Path { get; }
    public void WriteData(Utf8JsonWriter writer) => Data.WriteTo(writer);
    internal CacheDateTable Dates { get; }
    internal bool HasValue => JsonDisplayProjection.Truthy(new(Data));
    internal string ProjectId => project.Value;
    internal CacheRows Categories => categories.Value;
    internal bool IsFresh(DateTimeOffset nowUtc) => Access(() =>
        CacheJson.Timestamp(created.Value, Dates, out var timestamp) && (nowUtc - timestamp.ToUniversalTime()).TotalHours <= 24);
    internal T Access<T>(Func<T> action) => CacheFields.Access(Path, action, CategoryCacheReader.Invalid);
    internal void CheckRowQuota()
    {
        try { _ = categories.Value; }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Metadata.InvalidCategoryCache") { }
    }
}

public static class CategoryCacheReader
{
    public static CachedCategorySnapshot? Read(string configurationDirectory, ReferenceCacheReadLimits? limits = null)
    {
        limits ??= new();
        if (limits.InvalidProperty is { } invalid)
            throw new DomainException("Metadata.InvalidCategoryCacheReadPolicy", "Category-cache reader limits are invalid",
                "use positive byte/row quotas and a JSON depth between 1 and 1024", target: invalid);
        var path = System.IO.Path.Combine(System.IO.Path.GetFullPath(configurationDirectory), "last-categories.json");
        var decoded = LegacyCacheFile.Read(path, limits.DecodeLimits, Invalid, Limit);
        if (decoded is null) return null;
        var snapshot = new CachedCategorySnapshot(decoded, path, limits);
        snapshot.CheckRowQuota();
        return snapshot;
    }
    internal static DomainException Invalid(string path, string? details) => new("Metadata.InvalidCategoryCache",
        $"Category reference cache '{path}' is invalid", "modpack category list", "InvalidData", path, details: details);
    internal static DomainException Limit(string path, string details) => new("Metadata.CategoryCacheReadLimitExceeded",
        "Category reference cache exceeds the reader quota", "reduce the saved category list or use explicit category IDs",
        "LimitsExceeded", path, details: details);
}
