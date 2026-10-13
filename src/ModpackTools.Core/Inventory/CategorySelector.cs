using System.Globalization;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public static class CategorySelector
{
    public static string ResolveFromProject(string projectRoot, string projectId, string configurationDirectory,
        string selector, DateTimeOffset nowUtc, bool allowUnclassified = false, ReferenceCacheReadLimits? limits = null)
    {
        ValidateSelector(selector);
        if (Reserved(selector, allowUnclassified)) return "unclassified";
        var metadata = EditorialMetadataReader.Read(projectRoot);
        var cache = ReferenceCacheResolver.IsNumericSelector(selector) ? CategoryCacheReader.Read(configurationDirectory, limits) : null;
        return Resolve(metadata, cache, selector, projectId, nowUtc, allowUnclassified);
    }

    public static string Resolve(EditorialMetadata metadata, CachedCategorySnapshot? cache, string selector,
        string projectId, DateTimeOffset nowUtc, bool allowUnclassified = false)
    {
        ValidateSelector(selector);
        if (Reserved(selector, allowUnclassified)) return "unclassified";
        if (ReferenceCacheResolver.IsNumericSelector(selector))
        {
            if (cache is null || !cache.HasValue)
                throw new DomainException("Metadata.CategoryCacheNotFound", $"Category number '{selector}' cannot be resolved because no category list has been saved",
                    "modpack category list", "ObjectNotFound", selector);
            if (!cache.IsFresh(nowUtc))
                throw new DomainException("Metadata.CategoryCacheExpired", "The saved category list has expired", "modpack category list", "InvalidData");
            if (!string.Equals(cache.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Metadata.CategoryProjectMismatch", $"The saved category list belongs to project '{cache.ProjectId}', not '{projectId}'",
                    $"modpack -p {projectId} category list", "InvalidData", selector);
            var rows = cache.Categories;
            var matches = 0;
            CacheRowView selected = default;
            int? number = null;
            foreach (var row in rows.Enumerate())
            {
                var index = cache.Access(() => CacheJson.Integer(CacheFields.Field(row.Data, "Index"), cache.Dates));
                number ??= cache.Access(() => int.Parse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture));
                if (index == number) { matches++; selected = row; }
            }
            if (matches != 1)
            {
                if (rows.Count == 0)
                    throw new DomainException("Metadata.EmptyCategoryCache", $"Category number '{selector}' cannot be resolved because the saved list contains no categories",
                        "modpack category create <id>", "ObjectNotFound", selector);
                throw new DomainException("Metadata.CategoryReferenceOutOfRange", $"Category number '{selector}' does not exist; available range: 1-{rows.Count}",
                    "modpack category list", target: selector);
            }
            // Id is consumed on the selected value before the function returns;
            // unlike inventory references, there is no returned-array unwrap here.
            selector = cache.Access(() => CacheJson.String(CacheFields.Field(selected.Data, "Id"), cache.Dates));
            if (Reserved(selector, allowUnclassified)) return "unclassified";
        }
        foreach (var key in metadata.Categories.Entries.Keys)
            if (key.Length != 0 && string.Equals(key, selector, StringComparison.OrdinalIgnoreCase)) return key;
        throw new DomainException("Metadata.UnknownCategory", $"Category '{selector}' is not defined for project '{projectId}'",
            "modpack category list", target: selector);
    }

    private static void ValidateSelector(string selector)
    {
        if (string.IsNullOrEmpty(selector)) throw new ArgumentException("Supply a nonempty category selector.", nameof(selector));
    }
    private static bool Reserved(string selector, bool allow)
    {
        if (!string.Equals(selector, "unclassified", StringComparison.OrdinalIgnoreCase)) return false;
        if (allow) return true;
        throw new DomainException("Metadata.ReservedClassification", "The 'unclassified' classification cannot be removed",
            "assign affected mods with modpack category assign", "InvalidOperation", selector);
    }
}
