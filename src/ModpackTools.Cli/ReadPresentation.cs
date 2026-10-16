using ModpackTools.Core.Diagnostics;
using ModpackTools.Core.Inventory;

namespace ModpackTools.Cli;

// Optional paired-adapter read models. Machine-only domain data remains intact;
// compose these from the same owned observation instead of rereading the tree.
internal sealed record InventoryGroupRow(int Index, string Side, string? InvalidCategory);
internal sealed record InventoryGroupPresentation(string Name, InventoryGroupRow[] Rows);
internal sealed record InventoryPresentation(string Kind, InventoryGroupPresentation[] Groups, DoctorItem[] HealthItems)
{
    public static InventoryGroupPresentation[] Group(NumberedInventorySelection view)
    {
        var groups = view.Mods.Select((row, index) => (Item: row.Item, Index: index))
            .ToLookup(row => row.Item.Category ?? "unclassified", StringComparer.InvariantCultureIgnoreCase);
        var categories = view.Categories.Select(category => (category.Key, category.Name)).ToList();
        if (!categories.Any(category => StringComparer.InvariantCultureIgnoreCase.Equals(category.Key, "unclassified")))
            categories.Add(("unclassified", "UNCLASSIFIED"));
        return [.. categories.Where(category => groups.Contains(category.Key)).Select(category => new InventoryGroupPresentation(category.Name,
            [.. groups[category.Key].OrderBy(row => row.Item.Name, StringComparer.CurrentCultureIgnoreCase).Select(row => new InventoryGroupRow(row.Index,
                CanonicalSide(row.Item.Side), row.Item.InvalidCategory))]))];
    }
    private static string CanonicalSide(string value) => StringComparer.InvariantCultureIgnoreCase.Equals(value, "client") ? "client"
        : StringComparer.InvariantCultureIgnoreCase.Equals(value, "server") ? "server"
        : StringComparer.InvariantCultureIgnoreCase.Equals(value, "both") ? "both" : "unknown";
}
internal sealed record ConfigurationPresentation(string Kind, string Name, string Value, string? Source);
internal sealed record ConfigurationEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, EmptyData Data,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ConfigurationPresentation? Presentation = null);
internal sealed record SessionPresentation(string Kind, string? Name);
internal sealed record DiffPresentation(string Kind, PublicProject Project, string Baseline, DateTime BuiltUtc);
