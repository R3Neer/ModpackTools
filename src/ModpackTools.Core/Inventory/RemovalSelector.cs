using System.Collections.Immutable;
using System.Text;

namespace ModpackTools.Core.Inventory;

public static class RemovalSelector
{
    public static ImmutableArray<string> Resolve(RawInventorySnapshot snapshot, ImmutableArray<string> selectors, string type = "all")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (selectors.IsDefaultOrEmpty || selectors.Length > 1024 || selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > 4096)
            || (long)snapshot.Mods.Length + snapshot.Resources.Length + snapshot.Shaders.Length > 100000)
            throw new DomainException("Remove.InvalidRequest", "Removal selection exceeds its bounded request", "select bounded physical installed content");
        var normalized = InventorySelector.NormalizeType(type);
        var items = snapshot.Mods.Concat(snapshot.Resources).Concat(snapshot.Shaders).ToImmutableArray();
        long characters = 0;
        foreach (var item in items)
        {
            if (item is null || item.Id is null || item.Name is null) throw new DomainException("Remove.InvalidState", "Removal inventory is incomplete", "observe physical content again", "InvalidData");
            characters += (long)item.Id.Length + item.Name.Length + (item.Filename?.Length ?? 0) + (item.MetadataPath?.Length ?? 0);
            if (characters > 32 * 1024 * 1024) throw new DomainException("Remove.PlanningLimitExceeded", "Removal inventory text exceeds its quota", "select bounded physical content", "LimitsExceeded", "MaximumTextCharacters");
        }
        return ResolveItems([.. items.Where(item => normalized == InventoryType.All || normalized == InventoryType.Mod && item.Kind == ContentKind.Mod
            || normalized == InventoryType.ResourcePack && item.Kind == ContentKind.ResourcePack || normalized == InventoryType.ShaderPack && item.Kind == ContentKind.ShaderPack)], selectors, describe: Detail);
    }
    internal static ImmutableArray<string> ResolveItems(ImmutableArray<InventoryItem> items, ImmutableArray<string> selectors,
        Action<long>? work = null, Func<IEnumerable<string>, string>? describe = null)
    {
        var aliases = new PhysicalAliasIndex(); aliases.AddRange(items);
        var requested = ImmutableArray.CreateBuilder<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selector in selectors)
        {
            work?.Invoke(1);
            if (!aliases.TryGet(selector, out var matches)) throw new DomainException("Content.NotFound", $"Content '{selector}' was not found", "run modpack content list and select a physical installed item", "ObjectNotFound");
            if (matches.Count > 1) throw new DomainException("Content.AmbiguousSelector", $"Content selector '{selector}' is ambiguous", "use a stable ID or --type <mod|resourcepack|shaderpack>",
                details: (describe ?? Detail)(matches.Items.Select(item => item.Id)));
            var item = matches.First;
            if (item.Source is not (ContentSource.Packwiz or ContentSource.Local)) throw new DomainException("Content.NotRemovable", $"Content '{selector}' is not a removable file", "select the owning mod for built-in resources", "InvalidOperation");
            if (seen.Add(item.Id)) requested.Add(item.Id);
        }
        return requested.ToImmutable();
    }
    private static string Detail(IEnumerable<string> ids)
    {
        var text = new StringBuilder();
        foreach (var id in ids)
        {
            if ((long)text.Length + id.Length + 2 > 1024 * 1024) throw new DomainException("Remove.PlanningLimitExceeded", "Removal selector details exceed their quota", "use an unambiguous stable ID", "LimitsExceeded", "MaximumDetailCharacters");
            if (text.Length != 0) text.Append(", "); text.Append(id);
        }
        return text.ToString();
    }
}
