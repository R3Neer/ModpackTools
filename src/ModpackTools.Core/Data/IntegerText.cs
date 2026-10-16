using System.Globalization;

namespace ModpackTools.Core.Data;

// Scalar compatibility at data boundaries, not an expression evaluator.
internal static class IntegerText
{
    public static int Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return 0;
        try { return Literal(text); }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            // PowerShell falls back to the system integer converter after a
            // literal fails. Its hexadecimal path ignores lexical word width.
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Hexadecimal(text[2..].Length == 0 ? "0" : text[2..]);
            if (text.StartsWith("#", StringComparison.Ordinal)) return Hexadecimal(text[1..]);
            if (text.StartsWith("&h", StringComparison.OrdinalIgnoreCase)) return Hexadecimal(text[2..]);
            if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) throw;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
            return Convert.ToInt32(double.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture));
        }
    }

    private static int Hexadecimal(string digits)
    {
        try { return Convert.ToInt32(digits, 16); }
        catch (ArgumentException exception) { throw new FormatException(exception.Message, exception); }
    }

    private static int Literal(string text)
    {
        var position = text[0] is '+' or '-' ? 1 : 0;
        var negative = text[0] == '-';
        var radix = text.Length > position + 1 && text[position] == '0'
            ? char.ToLowerInvariant(text[position + 1]) switch { 'x' => 16, 'b' => 2, _ => 10 } : 10;
        if (radix != 10) position += 2;
        var start = position;
        while (position < text.Length && Digit(text[position], radix) >= 0) position++;
        if (radix == 10)
        {
            if (position < text.Length && text[position] == '.')
            {
                position++;
                while (position < text.Length && Digit(text[position], 10) >= 0) position++;
            }
            if (position < text.Length && text[position] is 'e' or 'E')
            {
                position++;
                if (position < text.Length && text[position] is '+' or '-') position++;
                while (position < text.Length && Digit(text[position], 10) >= 0) position++;
            }
        }
        if (position == start && radix != 16) throw new FormatException("Missing numeric digits.");
        var digits = text.AsSpan(start, position - start);
        // The PowerShell hexadecimal scanner prepends a zero, so even a bare
        // signed prefix or a prefix followed only by a valid suffix denotes zero.
        if (radix == 16 && digits.IsEmpty) digits = "0";
        var tail = text[position..].ToLowerInvariant();
        long multiplier = 1;
        if (tail.Length >= 2 && tail[^1] == 'b')
        {
            var power = tail[^2] switch { 'k' => 1, 'm' => 2, 'g' => 3, 't' => 4, 'p' => 5, _ => 0 };
            if (power != 0) { multiplier = 1L << (power * 10); tail = tail[..^2]; }
        }
        if (tail is not ("" or "y" or "uy" or "s" or "us" or "l" or "ul" or "u" or "n" or "d"))
            throw new FormatException("Invalid numeric suffix.");
        var unsigned = tail.StartsWith('u');
        decimal value;
        if (radix == 10)
        {
            var number = text.AsSpan(0, position);
            if (tail == "d") value = decimal.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture) * multiplier;
            else
            {
                var floating = double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture) * multiplier;
                if (!double.IsFinite(floating)) throw new OverflowException("The number is outside Int32.");
                value = (decimal)Math.Round(floating, MidpointRounding.ToEven);
            }
        }
        else
        {
            // Only sign-extension-sized magnitudes can eventually fit Int32.
            // Accumulate a negative word's complement rather than constructing
            // a potentially multi-megabyte BigInteger and then subtracting 2^N.
            var signedWord = !unsigned && (radix == 16
                ? digits.Length == (tail switch { "y" => 2, "s" => 4, "l" => 16, _ => digits.Length <= 8 ? 8 : 16 })
                : digits.Length is 8 or 16 or 32 or 64 or 96 || digits.Length >= 128 && digits.Length % 8 == 0);
            var complement = signedWord && Digit(digits[0], radix) >= radix / 2;
            ulong magnitude = 0;
            foreach (var character in digits)
            {
                var digit = Digit(character, radix);
                if (complement) digit = radix - 1 - digit;
                magnitude = checked(magnitude * (uint)radix + (uint)digit);
            }
            value = complement ? -(decimal)magnitude - 1 : magnitude;
            if (negative) value = -value;
            value = checked(value * multiplier);
        }
        // A suffix constrains the literal before the final Int32 cast. A bad
        // suffixed literal must not become successful by discarding its suffix.
        var valid = tail switch
        {
            "y" => value >= sbyte.MinValue && value <= sbyte.MaxValue,
            "uy" => value >= byte.MinValue && value <= byte.MaxValue,
            "s" => value >= short.MinValue && value <= short.MaxValue,
            "us" => value >= ushort.MinValue && value <= ushort.MaxValue,
            "l" => value >= long.MinValue && value <= long.MaxValue,
            "u" or "ul" => value >= 0 && value <= ulong.MaxValue,
            _ => true
        };
        if (!valid) throw new OverflowException("The number is outside its suffix type.");
        return Convert.ToInt32(value);
    }

    private static int Digit(char character, int radix)
    {
        var digit = character is >= '0' and <= '9' ? character - '0'
            : character is >= 'a' and <= 'f' ? character - 'a' + 10
            : character is >= 'A' and <= 'F' ? character - 'A' + 10 : -1;
        return digit < radix ? digit : -1;
    }
}
