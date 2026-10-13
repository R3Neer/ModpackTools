using System.Globalization;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public static class CategoryReferenceCache
{
    public static ReferenceCacheWriteResult Write(string configurationDirectory, CategoryListView view,
        DateTimeOffset createdUtc, ReferenceCacheWriteLimits? limits = null)
    {
        var maximumBytes = (limits ?? new()).MaximumBytes;
        if (maximumBytes <= 0)
            throw new DomainException("Metadata.InvalidCategoryCacheWritePolicy", "Category-cache byte quota must be positive",
                "use a positive category-cache byte quota", target: nameof(ReferenceCacheWriteLimits.MaximumBytes));
        if (view.Categories.IsDefault) throw new ArgumentException("Initialized category occurrences are required.", nameof(view));
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            throw new ArgumentException("A configuration directory is required.", nameof(configurationDirectory));
        var directory = Path.GetFullPath(configurationDirectory);
        var path = Path.Combine(directory, "last-categories.json");
        var timestamp = createdUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        AtomicJsonCacheFile.Write(directory, path, maximumBytes, writer =>
        {
            void Text(string field, string value)
            {
                writer.WriteString(field, value);
                if (writer.BytesPending >= 16 * 1024) writer.Flush();
            }
            writer.WriteStartObject();
            writer.WriteNumber("SchemaVersion", 1);
            Text("CreatedUtc", timestamp);
            Text("ProjectId", view.Project.Id);
            writer.WriteStartArray("Categories");
            foreach (var category in view.Categories)
            {
                writer.WriteStartObject();
                writer.WriteNumber("Index", category.ReferenceNumber);
                Text("Id", category.Id);
                Text("Name", category.Name);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }, () => new DomainException("Metadata.CategoryCacheWriteLimitExceeded", "Category references exceed the serialized cache byte quota",
            "reduce the category list or increase the cache byte quota", "LimitsExceeded", path),
            details => new DomainException("Metadata.CategoryCacheWriteFailed", $"Category reference cache '{path}' could not be written",
                "check configuration-directory permissions and retry modpack category list", "WriteError", path, details: details));
        return new(path, view.Categories.Length);
    }
}
