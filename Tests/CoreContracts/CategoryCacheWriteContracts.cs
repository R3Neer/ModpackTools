using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class CategoryCacheWriteContracts
{
    public static void Verify(RawInventorySnapshot snapshot)
    {
        var directory = Path.Combine(Path.GetTempPath(), "modpacktools-category-write-contracts-" + Guid.NewGuid().ToString("N"));
        var clock = new DateTimeOffset(2026, 10, 14, 10, 9, 10, TimeSpan.FromHours(2)).AddTicks(1234567);
        var view = new CategoryListView(snapshot.Project,
            [new("unclassified", "Custom 🎮", 1, 8, 7), new("unclassified", "UNCLASSIFIED", null, 2, -1, true)], 1);
        try
        {
            Expect(() => CategoryReferenceCache.Write(directory, view, clock, new(0)), "Metadata.InvalidCategoryCacheWritePolicy");
            if (Directory.Exists(directory)) throw new InvalidOperationException("Invalid writer policy created effects.");
            ExpectDefaultArray(() => CategoryReferenceCache.Write(directory, view with { Categories = default }, clock));
            if (Directory.Exists(directory)) throw new InvalidOperationException("Default category array created effects.");
            var written = CategoryReferenceCache.Write(directory, view, clock);
            var original = File.ReadAllBytes(written.Path);
            using (var data = JsonDocument.Parse(original))
            {
                var root = data.RootElement;
                var rows = root.GetProperty("Categories");
                if (written.Count != 2 || root.GetProperty("CreatedUtc").GetString() != "2026-10-14T08:09:10.1234567Z"
                    || rows[0].GetProperty("Index").GetInt32() != 7 || rows[1].GetProperty("Index").GetInt32() != -1
                    || rows[0].EnumerateObject().Count() != 3 || rows[0].GetProperty("Name").GetString() != "Custom 🎮"
                    || rows[0].GetProperty("Id").GetString() != rows[1].GetProperty("Id").GetString())
                    throw new InvalidOperationException("Category cache changed supplied numbers, text, duplicates or fields.");
            }
            CategoryReferenceCache.Write(directory, view, clock, new(original.Length));
            Expect(() => CategoryReferenceCache.Write(directory, view, clock, new(original.Length - 1)), "Metadata.CategoryCacheWriteLimitExceeded");
            using (var locked = new FileStream(written.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Expect(() => CategoryReferenceCache.Write(directory, view, clock), "Metadata.CategoryCacheWriteFailed");
            if (!File.ReadAllBytes(written.Path).SequenceEqual(original) || Directory.EnumerateFiles(directory, "*.tmp").Any())
                throw new InvalidOperationException("Failed category write changed old bytes or leaked a temporary.");
            CategoryReferenceCache.Write(directory, view with { Categories = [], DefinedCategoryCount = 0 }, clock);
            using var empty = JsonDocument.Parse(File.ReadAllBytes(written.Path));
            if (empty.RootElement.GetProperty("Categories").GetArrayLength() != 0)
                throw new InvalidOperationException("Initialized empty view no longer writes an empty category array.");
        }
        finally
        {
            var absolute = Path.GetFullPath(directory);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("modpacktools-category-write-contracts-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe category writer fixture cleanup.");
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
        }
    }

    private static void Expect(Func<ReferenceCacheWriteResult> action, string id)
    {
        try { action(); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return; }
        throw new InvalidOperationException("Expected category cache writer error " + id);
    }

    private static void ExpectDefaultArray(Func<ReferenceCacheWriteResult> action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Default category occurrences were accepted by the writer.");
    }
}
