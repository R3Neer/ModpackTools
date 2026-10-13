using System.Collections.Immutable;

namespace ModpackTools.Core.Versions;

// Closed tagged value: callers cannot introduce new variants or mutable children.
public sealed class VersionRange
{
    internal enum Shape { Text, Any, All, Unknown }
    internal Shape Kind { get; }
    internal string? Value { get; }
    internal ImmutableArray<VersionRange> Children { get; }
    private VersionRange(Shape kind, string? value, ImmutableArray<VersionRange> children)
    { Kind = kind; Value = value; Children = children; }
    public static VersionRange Text(string? value) => new(Shape.Text, value, []);
    public static VersionRange Unknown { get; } = new(Shape.Unknown, null, []);
    public static VersionRange Any(ImmutableArray<VersionRange> children) => Group(Shape.Any, children);
    public static VersionRange All(ImmutableArray<VersionRange> children) => Group(Shape.All, children);
    private static VersionRange Group(Shape kind, ImmutableArray<VersionRange> children)
    {
        if (children.IsDefault || children.Any(child => child is null))
            throw new ArgumentException("Range children must be initialized and non-null.", nameof(children));
        return new(kind, null, children);
    }
}

public sealed record VersionRangeLimits(int MaximumTextCharacters = 16 * 1024,
    int MaximumNodes = 4096, int MaximumComparisons = 10000, int MaximumDepth = 64,
    VersionComparisonLimits? Comparison = null);
