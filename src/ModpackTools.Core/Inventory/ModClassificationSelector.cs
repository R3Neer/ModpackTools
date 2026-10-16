using System.Collections.Immutable;

namespace ModpackTools.Core.Inventory;

public static class ModClassificationSelector
{
    public static ImmutableArray<InventoryItem> Resolve(RawInventorySnapshot snapshot, ImmutableArray<string> selectors)
    {
        if (selectors.IsDefaultOrEmpty || selectors.Length > 1024 || selectors.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Supply one through 1024 nonempty selector strings.", nameof(selectors));
        var aliases = new PhysicalAliasIndex();
        aliases.AddRange(snapshot.Mods);
        var output = ImmutableArray.CreateBuilder<InventoryItem>(selectors.Length);
        foreach (var selector in selectors)
        {
            if (!aliases.TryGet(selector, out var matches))
                throw new DomainException("Metadata.ModNotFound", $"Mod '{selector}' was not found in project '{snapshot.Project.Id}'",
                    "modpack content list --type mod", "ObjectNotFound", selector);
            if (matches.Count > 1)
            {
                var comparer = StringComparer.CurrentCultureIgnoreCase;
                var ids = string.Join(", ", matches.Items.Select(item => item.Id)
                    .Order(comparer).ThenBy(value => value, StringComparer.Ordinal).Distinct(comparer));
                throw new DomainException("Metadata.AmbiguousMod", $"Mod selector '{selector}' matches more than one mod",
                    "use an exact ID or filename", target: selector, details: "Matching IDs: " + ids);
            }
            output.Add(matches.First);
        }
        return output.MoveToImmutable();
    }
}
