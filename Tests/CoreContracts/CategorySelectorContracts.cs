using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class CategorySelectorContracts
{
    public static void Verify()
    {
        var directory = Path.Combine(Path.GetTempPath(), "modpacktools-category-contracts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, ".modpack"));
        var path = Path.Combine(directory, "last-categories.json");
        try
        {
            File.WriteAllText(Path.Combine(directory, ".modpack", "metadata.psd1"), "@{Categories=@{qol='unused invalid definition';'é'=@{};''=@{}}}");
            var metadata = EditorialMetadataReader.Read(directory);
            var clock = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
            const string normal = "{\"SchemaVersion\":99,\"CreatedUtc\":\"2026-10-13T12:00:00Z\",\"ProjectId\":\"fixture\",\"Categories\":[{\"Index\":1,\"Id\":\"qol\"}]}";
            File.WriteAllText(path, normal);
            var snapshot = CategoryCacheReader.Read(directory);
            File.WriteAllText(path, "broken replacement");
            if (CategorySelector.Resolve(metadata, snapshot, "1", "fixture", clock.AddHours(24)) != "qol")
                throw new InvalidOperationException("Category snapshot lost ownership, ignored schema semantics or exact24h freshness.");
            Expect(() => CategorySelector.Resolve(metadata, snapshot, "1", "fixture", clock.AddHours(24).AddTicks(1)), "Metadata.CategoryCacheExpired");
            Expect(() => CategorySelector.Resolve(metadata, snapshot, "e\u0301", "fixture", clock), "Metadata.UnknownCategory");
            Expect(() => CategorySelector.Resolve(metadata, snapshot, "UNCLASSIFIED", "fixture", clock), "Metadata.ReservedClassification");
            if (CategorySelector.Resolve(metadata, null, "UNCLASSIFIED", "fixture", clock, true) != "unclassified")
                throw new InvalidOperationException("Reserved category no longer returns canonical identity.");
            foreach (var categories in new[] { "null", "[{\"Index\":null,\"Id\":\"qol\"}]", "[{\"Index\":1,\"Id\":null}]", "[]" })
            {
                File.WriteAllText(path, normal.Replace("[{\"Index\":1,\"Id\":\"qol\"}]", categories, StringComparison.Ordinal));
                var cache = CategoryCacheReader.Read(directory);
                var id = categories == "null" ? "Metadata.InvalidCategoryCache"
                    : categories == "[]" ? "Metadata.EmptyCategoryCache"
                    : categories.Contains("\"Id\":null", StringComparison.Ordinal) ? "Metadata.UnknownCategory" : "Metadata.CategoryReferenceOutOfRange";
                Expect(() => CategorySelector.Resolve(metadata, cache, categories == "[]" ? "2147483648" : "1", "fixture", clock), id);
            }
            File.WriteAllText(Path.Combine(directory, ".modpack", "metadata.psd1"), "broken metadata");
            if (CategorySelector.ResolveFromProject(directory, "fixture", directory, "UNCLASSIFIED", clock, true, new(MaximumBytes: 0)) != "unclassified")
                throw new InvalidOperationException("Reserved category accessed metadata/cache or validated unused policy.");
        }
        finally
        {
            var absolute = Path.GetFullPath(directory);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("modpacktools-category-contracts-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe category fixture cleanup.");
            Directory.Delete(absolute, true);
        }
    }

    private static void Expect(Func<string> action, string id)
    {
        try { action(); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return; }
        throw new InvalidOperationException("Expected category selector error " + id);
    }
}
