using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Data;

internal abstract record TomlDeclarationValue;
internal sealed record TomlMissing : TomlDeclarationValue { public static TomlMissing Value { get; } = new(); }
internal sealed record TomlText(string Value) : TomlDeclarationValue;
internal sealed record TomlInteger(long Value) : TomlDeclarationValue;
internal sealed record TomlFloat(double Value) : TomlDeclarationValue;
internal sealed record TomlBoolean(bool Value) : TomlDeclarationValue;
internal sealed record TomlDateText(string Value, DateTimeOffset DateTime, int SecondPrecision, int Kind, string KindName) : TomlDeclarationValue;
internal sealed record TomlEntry(string Key, TomlDeclarationValue Value) : TomlDeclarationValue;
internal sealed record TomlSequence(ImmutableArray<TomlDeclarationValue> Items, bool TableArray = false, bool PipelineArray = false) : TomlDeclarationValue;
internal sealed record TomlFields : TomlDeclarationValue
{
    public ImmutableArray<KeyValuePair<string, TomlDeclarationValue>> Entries { get; }
    private readonly FrozenDictionary<string, TomlDeclarationValue> fields;
    public TomlFields(ImmutableArray<KeyValuePair<string, TomlDeclarationValue>> entries)
    { Entries = entries; fields = entries.ToFrozenDictionary(StringComparer.Ordinal); }
    public bool Contains(string key) => fields.ContainsKey(key);
    public TomlDeclarationValue Get(string key) => fields.GetValueOrDefault(key, TomlMissing.Value);
}

internal static class TomlDeclarationProjection
{
    public static TomlDeclarationValue Index(TomlDeclarationValue value, string key)
    {
        if (value is TomlFields table) return table.Get(key);
        if (value is TomlSequence array)
        {
            var index = IntegerText.Parse(key);
            if (index < 0) index += array.Items.Length;
            return index >= 0 && index < array.Items.Length ? array.Items[index] : TomlMissing.Value;
        }
        throw new InvalidOperationException("TOML value cannot be indexed by '" + key + "'.");
    }
    public static bool Contains(TomlDeclarationValue value, string key) => value is TomlFields table
        ? table.Contains(key) : throw new InvalidOperationException("TOML value has no table membership operation for '" + key + "'.");
    public static ImmutableArray<TomlDeclarationValue> Elements(TomlDeclarationValue value) => value switch
    {
        TomlMissing => [],
        TomlSequence array => array.Items,
        TomlFields table => [.. table.Entries.Select(entry => new TomlEntry(entry.Key, entry.Value))],
        _ => [value]
    };
    public static TomlDeclarationValue Pipeline(TomlDeclarationValue value)
    {
        var items = Elements(value);
        return items.Length switch { 0 => TomlMissing.Value, 1 => items[0], _ => new TomlSequence(items, PipelineArray: true) };
    }
    public static bool Truthy(TomlDeclarationValue value) => value switch
    {
        TomlMissing => false,
        TomlText text => text.Value.Length != 0,
        TomlInteger number => number.Value != 0,
        TomlFloat number => number.Value != 0,
        TomlBoolean boolean => boolean.Value,
        TomlSequence { PipelineArray: true } array => array.Items.Length switch { 0 => false, 1 => Truthy(array.Items[0]), _ => true },
        _ => true
    };
    public static bool EqualFalse(TomlDeclarationValue value)
    {
        if (value is not (TomlSequence or TomlFields)) return ScalarFalse(value);
        var matched = Elements(value).Where(ScalarFalse).ToImmutableArray();
        return Truthy(new TomlSequence(matched, PipelineArray: true));
    }
    private static bool ScalarFalse(TomlDeclarationValue value) => value switch
    {
        TomlBoolean boolean => !boolean.Value,
        TomlInteger number => number.Value == 0,
        TomlFloat number => number.Value == 0,
        TomlText text => string.Equals(text.Value, "False", StringComparison.InvariantCultureIgnoreCase),
        _ => false
    };
    public static bool NotNone(TomlDeclarationValue value)
    {
        bool Equal(TomlDeclarationValue item) => item is TomlBoolean { Value: true }
            || item is TomlText text && string.Equals(text.Value, "NONE", StringComparison.InvariantCultureIgnoreCase);
        return value is TomlSequence or TomlFields
            ? Truthy(new TomlSequence([.. Elements(value).Where(item => !Equal(item))], PipelineArray: true)) : !Equal(value);
    }
    public static string String(TomlDeclarationValue value) => value is TomlSequence or TomlFields
        ? string.Join(' ', Elements(value).Select(Scalar)) : Scalar(value);
    public static string RangeDisplay(TomlDeclarationValue value) => string.Join(" OR ", Elements(value).Select(Scalar));
    public static VersionRange Range(TomlDeclarationValue value) => value switch
    { TomlMissing => VersionRange.Text(null), TomlText text => VersionRange.Text(text.Value), _ => VersionRange.Unknown };
    public static ImmutableArray<string> FeatureKeys(TomlDeclarationValue value)
    {
        if (value is TomlFields table) return [.. table.Entries.Select(entry => entry.Key)];
        if (value is TomlSequence array && array.Items.Length != 0) return [.. array.Items.SelectMany(item => FeatureKeys(item))];
        throw new InvalidOperationException("Selected features value has no Keys property.");
    }
    private static string Scalar(TomlDeclarationValue value) => value switch
    {
        TomlMissing => "",
        TomlText text => text.Value,
        TomlDateText date => date.Value,
        TomlInteger number => number.Value.ToString(CultureInfo.InvariantCulture),
        TomlFloat number => number.Value.ToString("G15", CultureInfo.InvariantCulture),
        TomlBoolean boolean => boolean.Value ? "True" : "False",
        TomlFields => "Tomlyn.Model.TomlTable",
        TomlSequence array => array.PipelineArray ? "System.Object[]" : array.TableArray ? "Tomlyn.Model.TomlTableArray" : "Tomlyn.Model.TomlArray",
        TomlEntry entry => "[" + entry.Key + ", " + RuntimeString(entry.Value) + "]",
        _ => throw new InvalidOperationException("Unsupported TOML declaration value.")
    };
    private static string RuntimeString(TomlDeclarationValue value) => value switch
    {
        TomlFloat number => number.Value.ToString(CultureInfo.CurrentCulture),
        TomlInteger number => number.Value.ToString(CultureInfo.CurrentCulture),
        _ => Scalar(value)
    };
}
