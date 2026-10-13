using System.Collections.Immutable;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

internal static class EditorialProjection
{
    public static bool TryField(PsdValue entry, string key, out PsdValue value)
    {
        if (entry is PsdTable table) return table.Entries.TryGetValue(key, out value!);
        if (entry is PsdArray { Items.Length: 1 } single && single.Items[0] is PsdTable wrapped)
            return wrapped.Entries.TryGetValue(key, out value!);
        if (entry is PsdArray { Items.Length: > 1 } array && array.Items.All(child => child is PsdTable))
        {
            var values = ImmutableArray.CreateBuilder<PsdValue>();
            foreach (var child in array.Items.Cast<PsdTable>())
            {
                if (!child.Entries.TryGetValue(key, out var field))
                    throw new FormatException($"Every table in an override array must supply '{key}'.");
                if (field is PsdArray group) values.AddRange(group.Items); else values.Add(field);
            }
            value = values.Count == 1 ? values[0] : new PsdArray(values.ToImmutable());
            return true;
        }
        throw new FormatException("The override must supply a table or a supported array of tables.");
    }

    public static string Display(PsdValue value) => value switch
    {
        PsdArray array => string.Join(" ", array.Items.Select(item => item is PsdArray ? "System.Object[]" : Display(item))),
        PsdTable => "System.Collections.Hashtable",
        _ => DataScalars.String(value)
    };
}
