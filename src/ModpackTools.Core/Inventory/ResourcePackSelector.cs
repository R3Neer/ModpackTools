using System.Collections.Immutable;

namespace ModpackTools.Core.Inventory;

public sealed record ResourcePackTarget(string DefaultId, string Id, string Name, string? Filename, bool Active);

public static class ResourcePackSelector
{
    public static ImmutableArray<ResourcePackTarget> Resolve(RawInventorySnapshot snapshot, ImmutableArray<string> selectors)
    {
        if (selectors.IsDefaultOrEmpty || selectors.Length > 1024 || selectors.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Supply one through 1024 nonempty selector strings.", nameof(selectors));
        var aliases = new Dictionary<string, Matches>(StringComparer.OrdinalIgnoreCase);
        var represented = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        long occurrence = 0;
        void AddAlias(string? alias, ResourcePackTarget target)
        {
            if (string.IsNullOrEmpty(alias)) return;
            if (aliases.TryGetValue(alias, out var matches)) matches.Add(target, occurrence);
            else aliases.Add(alias, new(target, occurrence));
        }
        void Add(ResourcePackTarget target)
        {
            occurrence++;
            represented.Add(target.DefaultId);
            AddAlias(target.DefaultId, target);
            AddAlias(target.Id, target);
            AddAlias(target.Name, target);
            AddAlias(target.Filename, target);
        }
        foreach (var item in snapshot.ResourceView.Active) Add(new(item.Id, item.Id, item.Name, item.Filename, true));
        foreach (var item in snapshot.ResourceView.Inactive) Add(new("file/" + item.Filename, item.Id, item.Name, item.Filename, false));
        // Canonical ordering also chooses the representative when imported-culture
        // keys are distinct but invariant DefaultId membership considers them equal.
        foreach (var key in snapshot.Metadata.ResourcePacks.Entries.Keys.Order(StringComparer.Ordinal))
        {
            try
            {
                // Legacy StrictMode faults here before reading an extra's Name.
                if (occurrence == 0) throw new FormatException("DefaultId membership is unavailable because the activation candidate list is empty.");
                if (represented.Contains(key)) continue;
                var entry = snapshot.Metadata.ResourcePacks.Entries[key];
                var name = EditorialProjection.TryMemberField(entry, "Name", out var value) ? EditorialProjection.Display(value) : key;
                Add(new(key, key, name, null, false));
            }
            catch (FormatException exception)
            {
                throw new DomainException("ResourcePack.InvalidSelectorMetadata", $"Resource pack metadata entry '{key}' cannot supply a valid activation candidate",
                    "repair the matching ResourcePacks entry in .modpack/metadata.psd1", "InvalidData", key, details: exception.Message);
            }
        }
        var output = ImmutableArray.CreateBuilder<ResourcePackTarget>(selectors.Length);
        foreach (var selector in selectors)
        {
            if (!aliases.TryGetValue(selector, out var matches))
                throw new DomainException("ResourcePack.NotFound", $"Resource pack '{selector}' was not found",
                    "modpack content list --type resourcepack", "ObjectNotFound", selector);
            if (matches.Others is { } others)
            {
                var labels = new[] { matches.First }.Concat(others).Select(item => $"'{item.Name}' [{item.DefaultId}]");
                throw new DomainException("ResourcePack.AmbiguousSelector", $"Resource pack selector '{selector}' matches more than one pack",
                    "use an exact ID or filename", target: selector, details: "Matches: " + string.Join(", ", labels));
            }
            output.Add(matches.First);
        }
        return output.MoveToImmutable();
    }

    private sealed class Matches(ResourcePackTarget first, long occurrence)
    {
        private long lastOccurrence = occurrence;
        public ResourcePackTarget First => first;
        public List<ResourcePackTarget>? Others { get; private set; }
        public void Add(ResourcePackTarget target, long occurrence)
        {
            if (lastOccurrence == occurrence) return;
            lastOccurrence = occurrence;
            (Others ??= []).Add(target);
        }
    }
}
