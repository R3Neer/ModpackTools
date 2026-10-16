using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Numerics;

namespace ModpackTools.Core.Data;

// Input compatibility nodes remain private to the data/declaration boundary.
internal abstract class OrderedManifestValue;
internal sealed class ManifestNull : OrderedManifestValue { public static ManifestNull Value { get; } = new(); }
internal sealed class ManifestOmitted : OrderedManifestValue { public static ManifestOmitted Value { get; } = new(); }
internal sealed class ManifestString(string value) : OrderedManifestValue { public string Value { get; } = value; }
internal sealed class ManifestInteger(long value) : OrderedManifestValue { public long Value { get; } = value; }
internal sealed class ManifestBigInteger(BigInteger value) : OrderedManifestValue { public BigInteger Value { get; } = value; }
internal sealed class ManifestFloat(double value) : OrderedManifestValue { public double Value { get; } = value; }
internal sealed class ManifestBoolean(bool value) : OrderedManifestValue { public bool Value { get; } = value; }
internal sealed class ManifestDate(DateTime value) : OrderedManifestValue { public DateTime Value { get; } = value; }
internal sealed class ManifestArray(ImmutableArray<OrderedManifestValue> values) : OrderedManifestValue
{ public ImmutableArray<OrderedManifestValue> Values { get; } = values; }
internal sealed class ManifestMap : OrderedManifestValue
{
    public ImmutableArray<KeyValuePair<string, OrderedManifestValue>> Entries { get; }
    private readonly FrozenDictionary<string, OrderedManifestValue> fields;
    public ManifestMap(ImmutableArray<KeyValuePair<string, OrderedManifestValue>> entries)
    { Entries = entries; fields = entries.ToFrozenDictionary(StringComparer.Ordinal); }
    public bool TryGet(string name, out OrderedManifestValue value) => fields.TryGetValue(name, out value!);
}
