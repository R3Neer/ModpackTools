using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Data;

internal static class OrderedManifestProjection
{
    private const string MapType = "System.Management.Automation.OrderedHashtable";
    public static OrderedManifestValue Dot(OrderedManifestValue value, string name)
    {
        if (value is ManifestMap map && map.TryGet(name, out var field)) return field;
        if (value is ManifestArray array && array.Values.Length != 0)
        {
            var fields = ImmutableArray.CreateBuilder<OrderedManifestValue>();
            foreach (var child in array.Values)
            {
                var found = Dot(child, name);
                if (found is ManifestArray group) fields.AddRange(group.Values); else fields.Add(found);
            }
            return fields.Count == 1 ? fields[0] : new ManifestArray(fields.ToImmutable());
        }
        throw new InvalidOperationException("Required manifest property '" + name + "' is unavailable.");
    }
    public static OrderedManifestValue Index(OrderedManifestValue value, string name) => value is ManifestMap map
        ? map.TryGet(name, out var field) ? field : ManifestNull.Value
        : throw new InvalidOperationException("Manifest value cannot be indexed by '" + name + "'.");
    public static OrderedManifestValue Property(OrderedManifestValue value, string name)
    {
        var field = value is ManifestMap map && map.TryGet(name, out var found) ? found : ManifestNull.Value;
        return field is ManifestArray array ? array.Values.Length switch
        { 0 => ManifestNull.Value, 1 => array.Values[0], _ => field } : field;
    }
    public static bool Truthy(OrderedManifestValue value) => value switch
    {
        ManifestNull or ManifestOmitted => false,
        ManifestString text => text.Value.Length != 0,
        ManifestInteger integer => integer.Value != 0,
        ManifestBigInteger big => !big.Value.IsZero,
        ManifestFloat number => number.Value != 0,
        ManifestBoolean boolean => boolean.Value,
        ManifestArray array => array.Values.Length switch { 0 => false, 1 => Truthy(array.Values[0]), _ => true },
        _ => true
    };
    public static string String(OrderedManifestValue value) => value is ManifestArray array
        ? string.Join(' ', array.Values.Select(child => child is ManifestArray ? "System.Object[]" : Scalar(child))) : Scalar(value);
    private static string Scalar(OrderedManifestValue value) => value switch
    {
        ManifestNull or ManifestOmitted => "",
        ManifestString text => text.Value,
        ManifestInteger integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        ManifestBigInteger big => big.Value.ToString(CultureInfo.InvariantCulture),
        ManifestFloat number => number.Value.ToString("G15", CultureInfo.InvariantCulture),
        ManifestBoolean boolean => boolean.Value ? "True" : "False",
        ManifestDate date => date.Value.ToString(CultureInfo.InvariantCulture),
        ManifestMap => MapType,
        _ => "System.Object[]"
    };
    public static ImmutableArray<OrderedManifestValue> Elements(OrderedManifestValue value) => value is ManifestArray array ? array.Values : [value];
    public static bool EqualTrue(OrderedManifestValue value) => value is ManifestArray array
        ? array.Values.Any(ScalarEqualTrue) : ScalarEqualTrue(value);
    private static bool ScalarEqualTrue(OrderedManifestValue value) => value switch
    {
        ManifestBoolean boolean => boolean.Value,
        ManifestInteger integer => integer.Value == 1,
        ManifestBigInteger big => big.Value.IsOne,
        ManifestFloat number => number.Value == 1,
        ManifestString text => string.Equals(text.Value, "True", StringComparison.InvariantCultureIgnoreCase),
        _ => false
    };
    public static bool SchemaMismatch(OrderedManifestValue value)
    {
        if (value is not ManifestArray array) return !EqualOne(value);
        var unmatched = array.Values.Where(child => !EqualOne(child)).ToImmutableArray();
        return Truthy(new ManifestArray(unmatched));
    }
    private static bool EqualOne(OrderedManifestValue value) => value is ManifestString text ?
        string.Equals(text.Value, "1", StringComparison.InvariantCultureIgnoreCase) : ScalarEqualTrue(value);
    public static VersionRange Range(OrderedManifestValue value) => value switch
    {
        ManifestNull => VersionRange.Text(null),
        ManifestString text => VersionRange.Text(text.Value),
        ManifestArray array => VersionRange.Any([.. array.Values.Select(Range)]),
        ManifestMap => ObjectRange(value),
        _ => VersionRange.Unknown
    };
    private static VersionRange ObjectRange(OrderedManifestValue value)
    {
        var any = Property(value, "any");
        if (Truthy(any)) return VersionRange.Any([.. Elements(any).Select(Range)]);
        var all = Property(value, "all");
        return Truthy(all) ? VersionRange.All([.. Elements(all).Select(Range)]) : VersionRange.Unknown;
    }
    public static string RangeDisplay(OrderedManifestValue value) => string.Join(" OR ", Elements(value).Select(Scalar));
}
