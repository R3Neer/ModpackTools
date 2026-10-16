using System.Collections.Immutable;

namespace ModpackTools.Core.Inventory;

public static class BatchSelectorResolver
{
    private static readonly ReferenceLookupPolicy DefaultPolicy = new(["mod"]);

    public static ImmutableArray<string> Resolve(ImmutableArray<string> selectors, CachedInventorySnapshot? snapshot,
        string projectId, DateTimeOffset nowUtc, ReferenceLookupPolicy? policy = null) =>
        ResolveCore(selectors, () => snapshot, projectId, nowUtc, policy);

    public static ImmutableArray<string> ResolveFromDirectory(ImmutableArray<string> selectors, string configurationDirectory,
        string projectId, DateTimeOffset nowUtc, ReferenceLookupPolicy? policy = null, ReferenceCacheReadLimits? limits = null) =>
        ResolveCore(selectors, () => ReferenceCacheReader.Read(configurationDirectory, limits), projectId, nowUtc, policy);

    private static ImmutableArray<string> ResolveCore(ImmutableArray<string> selectors, Func<CachedInventorySnapshot?> readSnapshot,
        string projectId, DateTimeOffset nowUtc, ReferenceLookupPolicy? policy)
    {
        if (selectors.IsDefault || selectors.Length > 1024 || selectors.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Supply zero through 1024 nonempty selector strings.", nameof(selectors));
        policy ??= DefaultPolicy;
        var output = ImmutableArray.CreateBuilder<string>(selectors.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        CachedInventorySnapshot? snapshot = null;
        var loaded = false;
        foreach (var selector in selectors)
        {
            var value = selector;
            if (ReferenceCacheResolver.IsNumericSelector(selector) && !resolved.TryGetValue(selector, out value!))
            {
                if (!loaded) { snapshot = readSnapshot(); loaded = true; }
                value = ReferenceCacheResolver.ResolveSelector(snapshot, selector, projectId, nowUtc, policy)
                    ?? throw new InvalidOperationException("A numeric reference returned no selector.");
                resolved.Add(selector, value);
            }
            if (seen.Add(value)) output.Add(value);
        }
        return output.ToImmutable();
    }
}
