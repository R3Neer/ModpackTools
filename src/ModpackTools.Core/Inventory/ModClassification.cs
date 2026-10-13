using System.Collections.Immutable;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public static class ModClassification
{
    public static ImmutableArray<InventoryItem> Apply(ImmutableArray<InventoryItem> items, EditorialMetadata metadata)
    {
        var result = ImmutableArray.CreateBuilder<InventoryItem>(items.Length);
        foreach (var item in items)
        {
            if (item.Kind != ContentKind.Mod) { result.Add(item); continue; }
            var updated = item with { Category = "unclassified", InvalidCategory = null };
            if (metadata.Mods.Entries.TryGetValue(item.Id, out var entry) && Truthy(entry))
            {
                try
                {
                    if (EditorialProjection.TryField(entry, "Name", out var name)) updated = updated with { Name = EditorialProjection.Display(name) };
                    if (EditorialProjection.TryField(entry, "Side", out var side)) updated = updated with { Side = EditorialProjection.Display(side) };
                    if (EditorialProjection.TryField(entry, "Category", out var category))
                    {
                        var requested = EditorialProjection.Display(category);
                        updated = metadata.Categories.Entries.ContainsKey(requested)
                            ? updated with { Category = requested }
                            : updated with { InvalidCategory = requested };
                    }
                }
                catch (FormatException) { throw Invalid(item.Id); }
            }
            result.Add(updated);
        }
        return result.MoveToImmutable();
    }

    private static DomainException Invalid(string id) => new("Metadata.InvalidOverride",
        $"Mod override '{id}' cannot supply valid Name, Side and Category fields",
        "repair the matching Mods entry in .modpack/metadata.psd1", "InvalidData", id);

    private static bool Truthy(PsdValue value)
    {
        while (value is PsdArray { Items.Length: 1 } single) value = single.Items[0];
        return value switch
        {
            PsdNull => false,
            PsdBoolean flag => flag.Value,
            PsdString text => text.Value.Length != 0,
            PsdArray array => array.Items.Length != 0,
            PsdNumber number => PsdNumbers.Floating(number.Literal) != 0,
            _ => true
        };
    }

}
