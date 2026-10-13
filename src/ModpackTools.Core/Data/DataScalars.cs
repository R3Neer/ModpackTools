using System.Globalization;

namespace ModpackTools.Core.Data;

internal static class DataScalars
{
    public static string String(PsdValue value) => value switch
    {
        PsdString text => text.Value,
        PsdNull => "",
        PsdBoolean flag => flag.Value.ToString(),
        PsdNumber number => PsdNumbers.Display(number.Literal),
        _ => throw new FormatException("Expected a scalar data value.")
    };

    public static int Integer(PsdValue value) => value switch
    {
        PsdBoolean flag => flag.Value ? 1 : 0,
        PsdNull => 0,
        PsdNumber number when IsFloating(number.Literal) => Convert.ToInt32(double.Parse(number.Literal, CultureInfo.InvariantCulture)),
        PsdNumber number when decimal.TryParse(number.Literal, CultureInfo.InvariantCulture, out var integer) => Convert.ToInt32(integer),
        PsdNumber number => Convert.ToInt32(PsdNumbers.Floating(number.Literal)),
        PsdString text => IntegerText.Parse(text.Value),
        _ => throw new FormatException("Expected an integer data value.")
    };

    public static DiagnosticTarget? Target(PsdValue value) => value switch
    {
        PsdNull => null,
        PsdString text => new TextTarget(text.Value),
        PsdBoolean flag => new BooleanTarget(flag.Value),
        PsdNumber number when long.TryParse(number.Literal, CultureInfo.InvariantCulture, out var integer) => new NumberTarget(integer),
        PsdNumber number when !IsFloating(number.Literal) && decimal.TryParse(number.Literal, CultureInfo.InvariantCulture, out var integer) => new DecimalTarget(integer),
        PsdNumber number => FloatingDiagnostic(PsdNumbers.Floating(number.Literal)),
        _ => new TextTarget(String(value))
    };

    private static bool IsFloating(string literal) => literal.IndexOfAny(['.', 'e', 'E']) >= 0;

    private static DiagnosticTarget FloatingDiagnostic(double value) => double.IsFinite(value)
        ? new FloatingTarget(value)
        : new TextTarget(value.ToString(CultureInfo.InvariantCulture));

}
