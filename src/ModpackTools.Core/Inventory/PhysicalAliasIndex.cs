using System.Collections.Immutable;

namespace ModpackTools.Core.Inventory;

internal sealed class PhysicalAliasIndex
{
    private readonly Dictionary<string, Matches> aliases = new(StringComparer.OrdinalIgnoreCase);
    private long occurrence;

    public bool TryGet(string selector, out Matches matches) => aliases.TryGetValue(selector, out matches!);

    public void AddRange(ImmutableArray<InventoryItem> items)
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

    private void Add(string? alias, InventoryItem item)
    {
        if (string.IsNullOrEmpty(alias)) return;
        if (aliases.TryGetValue(alias, out var matches)) matches.Add(item, occurrence);
        else aliases.Add(alias, new(item, occurrence));
    }

    // Alias fields of one occurrence count once. Repeated identical objects and
    // equal records in separate occurrences must still make a selector ambiguous.
    internal sealed class Matches(InventoryItem first, long occurrence)
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
