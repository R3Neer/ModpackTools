using System.Collections.Immutable;
using System.Numerics;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Artifacts;

public sealed record ManifestRequirement(string Target, VersionRange Range, string RangeDisplay,
    string Kind, string Side, string Scope, VersionScheme Scheme, string Source, string SuggestedVersionId,
    ImmutableArray<ManifestRequirement> Any, ImmutableArray<ManifestRequirement> All, ManifestRequirement? Unless);
public sealed record ManifestAlias(ManifestAliasKey Key, string Version);
public sealed record DeclaredMod(string Id, string Version, string Side,
    ImmutableArray<ManifestAlias> Provides, ImmutableArray<ManifestRequirement> Requirements);
public enum RootManifestStatus { Complete, Missing, Incomplete }
public sealed record ManifestDiagnostic(string Code, string Message, string Manifest, string? Details, string? Hint);
public sealed record RootManifestDeclarations(ImmutableArray<DeclaredMod> Mods,
    ImmutableArray<ManifestDiagnostic> Diagnostics, ImmutableArray<string> NestedPaths, RootManifestStatus Status, bool CanTraverseNested);

public sealed record RootManifestLimits(int MaximumTextCharacters = 4 * 1024 * 1024,
    int MaximumTokenCharacters = 4 * 1024 * 1024, int MaximumNodes = 100000, int MaximumJsonDepth = 64,
    int MaximumRequirements = 100000, int MaximumRequirementDepth = 64,
    int MaximumAliases = 100000, int MaximumNestedPaths = 100000);

public enum ManifestAliasKeyKind { String, Int64, BigInteger, Double, Boolean, Date, Array, Object }

// Non-string source keys never acquire string-target identity through formatting.
public sealed class ManifestAliasKey : IEquatable<ManifestAliasKey>
{
    public ManifestAliasKeyKind Kind { get; }
    public string? StringValue { get; }
    public long? IntegerValue { get; }
    public BigInteger? BigIntegerValue { get; }
    public double? FloatingValue { get; }
    public bool? BooleanValue { get; }
    public DateTime? DateValue { get; }
    public string? OpaqueJson { get; }
    private readonly object? identity;
    private ManifestAliasKey(ManifestAliasKeyKind kind, string? text = null, long? integer = null,
        BigInteger? big = null, double? floating = null, bool? boolean = null, DateTime? date = null,
        object? identity = null, string? opaqueJson = null)
    {
        Kind = kind; StringValue = text; IntegerValue = integer; BigIntegerValue = big;
        FloatingValue = floating; BooleanValue = boolean; DateValue = date;
        this.identity = identity; OpaqueJson = opaqueJson;
    }
    internal static ManifestAliasKey Text(string value) => new(ManifestAliasKeyKind.String, text: value);
    internal static ManifestAliasKey Integer(long value) => new(ManifestAliasKeyKind.Int64, integer: value);
    internal static ManifestAliasKey Big(BigInteger value) => new(ManifestAliasKeyKind.BigInteger, big: value);
    internal static ManifestAliasKey Floating(double value) => new(ManifestAliasKeyKind.Double, floating: value);
    internal static ManifestAliasKey Boolean(bool value) => new(ManifestAliasKeyKind.Boolean, boolean: value);
    internal static ManifestAliasKey Date(DateTime value) => new(ManifestAliasKeyKind.Date, date: value);
    internal static ManifestAliasKey Opaque(bool array, object identity, string json) => new(
        array ? ManifestAliasKeyKind.Array : ManifestAliasKeyKind.Object, identity: identity, opaqueJson: json);
    public bool Equals(ManifestAliasKey? other) => other is not null && Kind == other.Kind && (Kind switch
    {
        ManifestAliasKeyKind.String => StringComparer.OrdinalIgnoreCase.Equals(StringValue, other.StringValue),
        ManifestAliasKeyKind.Int64 => IntegerValue == other.IntegerValue,
        ManifestAliasKeyKind.BigInteger => BigIntegerValue == other.BigIntegerValue,
        ManifestAliasKeyKind.Double => FloatingValue.Equals(other.FloatingValue),
        ManifestAliasKeyKind.Boolean => BooleanValue == other.BooleanValue,
        ManifestAliasKeyKind.Date => DateValue == other.DateValue,
        _ => ReferenceEquals(identity, other.identity)
    });
    public override bool Equals(object? obj) => obj is ManifestAliasKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, Kind switch
    {
        ManifestAliasKeyKind.String => StringComparer.OrdinalIgnoreCase.GetHashCode(StringValue!),
        ManifestAliasKeyKind.Int64 => IntegerValue.GetHashCode(),
        ManifestAliasKeyKind.BigInteger => BigIntegerValue.GetHashCode(),
        ManifestAliasKeyKind.Double => FloatingValue.GetHashCode(),
        ManifestAliasKeyKind.Boolean => BooleanValue.GetHashCode(),
        ManifestAliasKeyKind.Date => DateValue.GetHashCode(),
        _ => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(identity!)
    });
}
