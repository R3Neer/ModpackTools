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
        var aliases = new Dictionary<string, Matches>(StringComparer.OrdinalIgnoreCase);
        long occurrence = 0;
        void Add(string? alias, InventoryItem item)
        {
            if (string.IsNullOrEmpty(alias)) return;
            if (aliases.TryGetValue(alias, out var matches)) matches.Add(item, occurrence);
            else aliases.Add(alias, new(item, occurrence));
        }
        void Index(ImmutableArray<InventoryItem> items)
        {
            foreach (var item in items)
            {
                occurrence++;
                Add(item.Name, item);
                Add(item.Id, item);
                Add(item.Filename, item);
                if (!string.IsNullOrEmpty(item.MetadataPath))
                {
                    var stem = Path.GetFileName(item.MetadataPath);
                    // The legacy regex's $ also matches before one final LF.
                    var end = stem.EndsWith('\n') ? stem.Length - 1 : stem.Length;
                    if (stem.AsSpan(0, end).EndsWith(".pw.toml", StringComparison.OrdinalIgnoreCase))
                        stem = stem[..(end - 8)] + stem[end..];
                    Add(stem, item);
                }
            }
        }
        if (normalized is InventoryType.All or InventoryType.Mod) Index(snapshot.Mods);
        if (normalized is InventoryType.All or InventoryType.ResourcePack) Index(snapshot.Resources);
        if (normalized is InventoryType.All or InventoryType.ShaderPack) Index(snapshot.Shaders);

        var selected = ImmutableArray.CreateBuilder<InventoryItem>();
        var paths = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var selector in selectors)
        {
            if (!aliases.TryGetValue(selector, out var matches))
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

    // Alias fields of one occurrence count once. Repeated identical objects and
    // equal records in separate occurrences must still make a selector ambiguous.
    private sealed class Matches(InventoryItem first, long occurrence)
    {
        private long lastOccurrence = occurrence;
        private List<InventoryItem>? others;
        public InventoryItem First => first;
        public int Count => 1 + (others?.Count ?? 0);
        public IEnumerable<InventoryItem> Items
        {
            get
            {
                yield return first;
                if (others is not null) foreach (var item in others) yield return item;
            }
        }
        public void Add(InventoryItem item, long occurrence)
        {
            if (lastOccurrence == occurrence) return;
            lastOccurrence = occurrence;
            (others ??= []).Add(item);
        }
    }
}
