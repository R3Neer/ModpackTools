using System.Collections.Immutable;

namespace ModpackTools.Core.Inventory;

public sealed record InventoryReference(int Index, string Id, ContentKind Kind, string Name,
    string Filename, InventorySource Source, string Selector);
public sealed record NumberedPhysical(InventoryItem Item, int? Number);
public sealed record NumberedActive(ActiveResource Item, int Number);
public sealed record NumberedInventorySelection(InventorySelection Selection,
    ImmutableArray<CategoryDefinition> Categories, ImmutableArray<NumberedPhysical> Mods,
    ImmutableArray<NumberedActive> ActiveResources, ImmutableArray<NumberedPhysical> InactiveResources,
    ImmutableArray<NumberedPhysical> Shaders, ImmutableArray<InventoryReference> References);

public static class InventoryReferences
{
    public static NumberedInventorySelection Assign(InventorySelection selection)
    {
        var categories = CategoryDefinitions.Read(selection.Metadata);
        var groups = selection.Mods.Where(item => item.Category is not null)
            .GroupBy(item => item.Category!, StringComparer.InvariantCultureIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToImmutableArray(),
                StringComparer.InvariantCultureIgnoreCase);
        var references = ImmutableArray.CreateBuilder<InventoryReference>();
        var numbers = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        void Add(object identity, string id, ContentKind kind, string name, string? filename, InventorySource source)
        {
            var index = checked(references.Count + 1);
            var selector = kind == ContentKind.ResourcePack && !string.IsNullOrEmpty(filename) ? filename : id;
            references.Add(new(index, id, kind, name, filename ?? "", source, selector));
            numbers[identity] = index;
        }
        void AddPhysical(InventoryItem item) => Add(item, item.Id, item.Kind, item.Name, item.Filename,
            item.Source switch
            {
                ContentSource.Packwiz => InventorySource.Packwiz,
                ContentSource.Local => InventorySource.Local,
                _ => throw new ArgumentException("Unknown physical content source.", nameof(selection))
            });
        void AddGroup(string key)
        {
            if (groups.TryGetValue(key, out var items)) foreach (var item in items) AddPhysical(item);
        }
        foreach (var category in categories) AddGroup(category.Key);
        AddGroup("unclassified");
        foreach (var item in selection.ActiveResources)
            Add(item, item.Id, ContentKind.ResourcePack, item.Name, item.Filename, item.Source switch
            {
                ActiveResourceSource.Packwiz => InventorySource.Packwiz,
                ActiveResourceSource.Local => InventorySource.Local,
                ActiveResourceSource.Builtin => InventorySource.Builtin,
                ActiveResourceSource.Missing => InventorySource.Missing,
                _ => throw new ArgumentException("Unknown active resource source.", nameof(selection))
            });
        foreach (var item in selection.InactiveResources.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)) AddPhysical(item);
        foreach (var item in selection.Shaders.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)) AddPhysical(item);
        NumberedPhysical Number(InventoryItem item) => new(item, numbers.TryGetValue(item, out var number) ? number : null);
        return new(selection, categories, selection.Mods.Select(Number).ToImmutableArray(),
            selection.ActiveResources.Select(item => new NumberedActive(item, numbers[item])).ToImmutableArray(),
            selection.InactiveResources.Select(Number).ToImmutableArray(), selection.Shaders.Select(Number).ToImmutableArray(),
            references.ToImmutable());
    }
}
