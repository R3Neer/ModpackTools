using System.Collections.Immutable;

namespace ModpackTools.Core.Inventory;

public enum ActiveResourceSource { Packwiz, Local, Builtin, Missing }
public sealed record ActiveResource(string Id, string Name, string? Filename, ActiveResourceSource Source, int Priority);
public sealed record ResourcePackView(ImmutableArray<ActiveResource> Active, ImmutableArray<InventoryItem> Inactive);

public static class ResourcePackActivation
{
    public static ResourcePackView Compose(ImmutableArray<InventoryItem> resources,
        ImmutableArray<string> activeIds, EditorialMetadata metadata)
    {
        var physical = new Dictionary<string, InventoryItem>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var item in resources)
        {
            if (item.Kind != ContentKind.ResourcePack) throw new ArgumentException("Resource activation requires resource-pack rows.", nameof(resources));
            if (item.Filename is not null) physical.TryAdd(item.Filename, item);
        }
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var activeFiles = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var active = ImmutableArray.CreateBuilder<ActiveResource>(activeIds.Length);
        foreach (var id in activeIds)
        {
            var filename = id.StartsWith("file/", StringComparison.OrdinalIgnoreCase) ? id[5..] : null;
            var hasFilename = !string.IsNullOrEmpty(filename);
            var item = hasFilename && physical.TryGetValue(filename!, out var found) ? found : null;
            var name = item?.Name ?? (hasFilename ? filename! : id);
            if (!overrides.TryGetValue(id, out var overridden))
            {
                overridden = null;
                if (metadata.ResourcePacks.Entries.TryGetValue(id, out var entry))
                {
                    try
                    {
                        if (EditorialProjection.TryMemberField(entry, "Name", out var value)) overridden = EditorialProjection.Display(value);
                    }
                    catch (FormatException exception)
                    {
                        throw new DomainException("Metadata.InvalidResourceOverride", $"Resource override '{id}' cannot supply a valid Name field",
                            "repair the matching ResourcePacks entry in .modpack/metadata.psd1", "InvalidData", id, details: exception.Message);
                    }
                }
                overrides.Add(id, overridden);
            }
            if (overridden is not null) name = overridden;
            var source = item?.Source switch
            {
                ContentSource.Packwiz => ActiveResourceSource.Packwiz,
                ContentSource.Local => ActiveResourceSource.Local,
                null => hasFilename ? ActiveResourceSource.Missing : ActiveResourceSource.Builtin,
                _ => throw new ArgumentException("Unknown physical content source.", nameof(resources))
            };
            active.Add(new(id, name, filename, source, active.Count + 1));
            if (hasFilename) activeFiles.Add(filename!);
        }
        var inactive = resources.Where(item => item.Filename is null || !activeFiles.Contains(item.Filename))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToImmutableArray();
        return new(active.MoveToImmutable(), inactive);
    }
}
