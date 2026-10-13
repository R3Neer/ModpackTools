using System.Collections.Immutable;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public sealed record CategoryDefinition(string Key, string Name, int Order);

public static class CategoryDefinitions
{
    public static ImmutableArray<CategoryDefinition> Read(EditorialMetadata metadata)
    {
        var definitions = ImmutableArray.CreateBuilder<CategoryDefinition>(metadata.Categories.Entries.Count);
        foreach (var (key, entry) in metadata.Categories.Entries)
            definitions.Add(Extract(key, entry));
        return definitions.MoveToImmutable().OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(definition => definition.Key, StringComparer.Ordinal).ToImmutableArray();
    }

    internal static CategoryDefinition Extract(string key, PsdValue entry)
    {
        try
        {
            var name = EditorialProjection.TryMemberField(entry, "Name", out var nameField)
                ? EditorialProjection.Display(nameField) : key.ToUpperInvariant();
            var order = EditorialProjection.TryMemberField(entry, "Order", out var orderField)
                ? DataScalars.Integer(orderField) : 1000;
            return new(key, name, order);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new DomainException("Metadata.InvalidCategoryDefinition",
                $"Category definition '{key}' cannot supply valid Name and Order fields",
                "repair the matching Categories entry in .modpack/metadata.psd1", "InvalidData", key);
        }
    }
}
