using System.Collections.Immutable;

namespace ModpackTools.Core.Inventory;

public static class UpdateTargetSelector
{
    public static ImmutableArray<InventoryItem> Resolve(RawInventorySnapshot snapshot,
        ImmutableArray<string> selectors, string? type = "all")
    {
        if (selectors.IsDefaultOrEmpty || selectors.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Supply a nonempty collection of nonempty selectors.", nameof(selectors));
        var normalized = InventorySelector.NormalizeType(type ?? "");
        var aliases = new PhysicalAliasIndex();
        if (normalized is InventoryType.All or InventoryType.Mod) aliases.AddRange(snapshot.Mods);
        if (normalized is InventoryType.All or InventoryType.ResourcePack) aliases.AddRange(snapshot.Resources);
        if (normalized is InventoryType.All or InventoryType.ShaderPack) aliases.AddRange(snapshot.Shaders);

        var selected = ImmutableArray.CreateBuilder<InventoryItem>();
        var paths = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var selector in selectors)
        {
            if (!aliases.TryGet(selector, out var matches))
                throw new DomainException("Content.NotFound", $"Content '{selector}' was not found in project '{snapshot.Project.Id}'",
                    "modpack content list", "ObjectNotFound", selector);
            if (matches.Count > 1)
            {
                var comparer = StringComparer.CurrentCultureIgnoreCase;
                var ids = string.Join(", ", matches.Items.Select(item => Kind(item.Kind) + ":" + item.Id)
                    .Order(comparer).ThenBy(value => value, StringComparer.Ordinal).Distinct(comparer));
                throw new DomainException("Content.AmbiguousSelector", $"Content selector '{selector}' matches more than one item",
                    "add --type <mod|resourcepack|shaderpack>", target: selector, details: "Matching entries: " + ids);
            }
            var item = matches.First;
            var path = item.MetadataPath;
            if (item.Source != ContentSource.Packwiz || string.IsNullOrEmpty(path))
                throw new DomainException("Content.LocalNotUpdatable", $"Content '{selector}' is local and cannot be updated by Packwiz",
                    "replace the local file manually or select Packwiz-managed content", "InvalidOperation", selector);
            if (paths.Add(path)) selected.Add(item);
        }
        return selected.ToImmutable();
    }

    private static string Kind(ContentKind kind) => kind switch
    {
        ContentKind.Mod => "mod",
        ContentKind.ResourcePack => "resourcepack",
        ContentKind.ShaderPack => "shaderpack",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

}
