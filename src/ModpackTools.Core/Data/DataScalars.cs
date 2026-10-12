using System.Globalization;

namespace ModpackTools.Core.Data;

internal static class DataScalars
{
    public static string String(PsdValue value) => value switch
    {
        PsdString text => text.Value,
        PsdNull => "",
        PsdBoolean flag => flag.Value.ToString(),
        PsdNumber number => NumberString(number.Literal),
        _ => throw new FormatException("Expected a scalar data value.")
    };

    public static int Integer(PsdValue value) => value switch
    {
        PsdBoolean flag => flag.Value ? 1 : 0,
        PsdNull => 0,
        PsdNumber number when IsFloating(number.Literal) => Convert.ToInt32(double.Parse(number.Literal, CultureInfo.InvariantCulture)),
        PsdNumber number when decimal.TryParse(number.Literal, CultureInfo.InvariantCulture, out var integer) => Convert.ToInt32(integer),
        PsdNumber number => Convert.ToInt32(double.Parse(number.Literal, CultureInfo.InvariantCulture)),
        PsdString text => StringInteger(text.Value),
        _ => throw new FormatException("Expected an integer data value.")
    };

    public static DiagnosticTarget? Target(PsdValue value) => value switch
    {
        PsdNull => null,
        PsdString text => new TextTarget(text.Value),
        PsdBoolean flag => new BooleanTarget(flag.Value),
        PsdNumber number when long.TryParse(number.Literal, CultureInfo.InvariantCulture, out var integer) => new NumberTarget(integer),
        PsdNumber number when !IsFloating(number.Literal) && decimal.TryParse(number.Literal, CultureInfo.InvariantCulture, out var integer) => new DecimalTarget(integer),
        PsdNumber number => FloatingDiagnostic(double.Parse(number.Literal, CultureInfo.InvariantCulture)),
        _ => new TextTarget(String(value))
    };

    private static bool IsFloating(string literal) => literal.IndexOfAny(['.', 'e', 'E']) >= 0;

    private static string NumberString(string literal) => !IsFloating(literal) && decimal.TryParse(literal, CultureInfo.InvariantCulture, out var integer)
        ? integer.ToString(CultureInfo.InvariantCulture)
        : double.Parse(literal, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static DiagnosticTarget FloatingDiagnostic(double value) => double.IsFinite(value)
        ? new FloatingTarget(value)
        : new TextTarget(value.ToString(CultureInfo.InvariantCulture));

    private static int StringInteger(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return 0;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt32(text[2..], 16);
        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt32(text[2..], 2);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        return Convert.ToInt32(double.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture));
    }
}
