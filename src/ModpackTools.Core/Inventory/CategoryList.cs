using System.Collections.Immutable;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Inventory;

public sealed record EditorialCategory(string Id, string Name, int Order, int ModCount);
public sealed record CategoryListEntry(string Id, string Name, int? Order, int ModCount,
    int ReferenceNumber, bool IsUnclassified = false);
public sealed record CategoryListView(ProjectContext Project, ImmutableArray<CategoryListEntry> Categories,
    int DefinedCategoryCount);

public static class CategoryList
{
    public static ImmutableArray<EditorialCategory> Prepare(EditorialMetadata metadata)
    {
        if (metadata.Categories.Entries.Count == 0) return [];
        // Assignment projection is independent of the requested category. Validate
        // it once before definitions, then reuse counts instead of repeated scans.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, entry) in metadata.Mods.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            try
            {
                if (!EditorialProjection.TryMemberField(entry, "Category", out var field)) continue;
                var category = EditorialProjection.Display(field);
                counts[category] = counts.GetValueOrDefault(category) + 1;
            }
            catch (FormatException exception)
            {
                throw new DomainException("Metadata.InvalidCategoryAssignment",
                    $"Mod assignment '{key}' cannot supply a valid Category field",
                    "repair the matching Mods entry in .modpack/metadata.psd1", "InvalidData", key, details: exception.Message);
            }
        }
        var rows = ImmutableArray.CreateBuilder<EditorialCategory>(metadata.Categories.Entries.Count);
        foreach (var (key, entry) in metadata.Categories.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var definition = CategoryDefinitions.Extract(key, entry);
            rows.Add(new(key, definition.Name, definition.Order, counts.GetValueOrDefault(key)));
        }
        return rows.MoveToImmutable().OrderBy(row => row.Order)
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Id, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Id, StringComparer.Ordinal).ToImmutableArray();
    }

    public static CategoryListView Compose(ProjectContext project, ImmutableArray<EditorialCategory> definitions,
        ImmutableArray<InventoryItem> mods)
    {
        if (definitions.IsDefault || mods.IsDefault)
            throw new ArgumentException("Initialized category definitions and mod occurrences are required.");
        var rows = ImmutableArray.CreateBuilder<CategoryListEntry>(checked(definitions.Length + 1));
        foreach (var definition in definitions)
            rows.Add(new(definition.Id, definition.Name, definition.Order, definition.ModCount, rows.Count + 1));
        rows.Add(new("unclassified", "UNCLASSIFIED", null,
            mods.Count(mod => string.Equals(mod.Category, "unclassified", StringComparison.InvariantCultureIgnoreCase)),
            rows.Count + 1, true));
        return new(project, rows.MoveToImmutable(), definitions.Length);
    }

    public static CategoryListView Read(ProjectContext project, InventoryReadLimits? limits = null)
    {
        var metadata = EditorialMetadataReader.Read(project);
        var definitions = Prepare(metadata);
        var inventory = InventoryReader.Read(project, metadata, limits);
        return Compose(project, definitions, inventory.Mods);
    }
}
