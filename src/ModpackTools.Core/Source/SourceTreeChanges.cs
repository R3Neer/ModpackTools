using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace ModpackTools.Core.Source;

public sealed record SourceChange(string Path, string? Before, string? After);

public static class SourceTreeChanges
{
    public static ImmutableArray<SourceChange> Compare(SourceTreeSnapshot before, SourceTreeSnapshot after, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return CompareCore(before.Files, after.Files, static value => value, culture ?? CultureInfo.CurrentCulture);
    }

    public static ImmutableArray<SourceChange> Compare(ImmutableDictionary<string, string?> before,
        ImmutableDictionary<string, string?> after, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return CompareCore(Normalize(before), Normalize(after), static value => value, culture ?? CultureInfo.CurrentCulture);
    }

    private static IReadOnlyDictionary<string, string?> Normalize(ImmutableDictionary<string, string?> input)
    {
        if (ReferenceEquals(input.KeyComparer, StringComparer.OrdinalIgnoreCase)) return input;
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, hash) in input)
        {
            if (!normalized.TryAdd(path, hash))
                throw SourceTree.Collision(path, normalized.Keys.First(key => string.Equals(key, path, StringComparison.OrdinalIgnoreCase)));
        }
        return normalized.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static ImmutableArray<SourceChange> CompareCore<T>(IReadOnlyDictionary<string, T> before,
        IReadOnlyDictionary<string, T> after, Func<T, string?> value, CultureInfo culture)
    {
        var paths = new HashSet<string>(before.Keys, StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(after.Keys);
        var result = ImmutableArray.CreateBuilder<SourceChange>();
        foreach (var path in paths.OrderBy(path => path, StringComparer.Create(culture, true)).ThenBy(path => path, StringComparer.Ordinal))
        {
            var oldHash = before.TryGetValue(path, out var old) ? value(old) : null;
            var newHash = after.TryGetValue(path, out var next) ? value(next) : null;
            if (!string.Equals(oldHash, newHash, StringComparison.Ordinal)) result.Add(new(path, oldHash, newHash));
        }
        return result.ToImmutable();
    }
}
