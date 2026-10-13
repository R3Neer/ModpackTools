using System.Globalization;
using System.Numerics;

namespace ModpackTools.Core.Data;

public static class PsdNumbers
{
    public static double Floating(string literal)
    {
        if (literal.IndexOfAny(['.', 'e', 'E']) >= 0)
            return double.Parse(literal, CultureInfo.InvariantCulture);
        var digits = literal.AsSpan();
        var negative = digits.Length > 0 && digits[0] == '-';
        if (digits.Length > 0 && digits[0] is '+' or '-') digits = digits[1..];
        var leading = 0;
        while (leading < digits.Length && digits[leading] == '0') leading++;
        digits = digits[leading..];
        if (digits.Length == 0) return 0;
        if (digits.Length > 309) return negative ? double.NegativeInfinity : double.PositiveInfinity;
        var value = (double)BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    public static string Display(string literal) =>
        literal.IndexOfAny(['.', 'e', 'E']) < 0 && decimal.TryParse(literal, CultureInfo.InvariantCulture, out var integer)
            ? integer.ToString(CultureInfo.InvariantCulture)
            : Floating(literal).ToString("G15", CultureInfo.InvariantCulture);
}
