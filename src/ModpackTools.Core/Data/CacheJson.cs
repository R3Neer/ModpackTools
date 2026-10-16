using System.Globalization;
using System.Text.Json;

namespace ModpackTools.Core.Data;

internal static class CacheJson
{
    public static string String(JsonDisplayValue value, CacheDateTable dates) => value.Group is { } group
        ? string.Join(" ", group.Select(item => NestedString(item, dates))) : String(value.Scalar, dates);

    private static string String(JsonElement value, CacheDateTable dates) => value.ValueKind switch
    {
        JsonValueKind.Array => string.Join(" ", value.EnumerateArray().Select(item => NestedString(item, dates))),
        JsonValueKind.Object => ObjectString(value, dates),
        _ => ScalarString(value, dates)
    };
    private static string NestedString(JsonElement value, CacheDateTable dates) => value.ValueKind switch
    {
        JsonValueKind.Array => "System.Object[]",
        JsonValueKind.Object => "",
        _ => ScalarString(value, dates)
    };
    private static string ObjectString(JsonElement value, CacheDateTable dates)
    {
        var fields = JsonDisplayProjection.Properties(value);
        return fields.Count == 0 ? "" : "@{" + string.Join("; ", fields.Select(pair => pair.Key + "=" + NestedString(pair.Value, dates))) + "}";
    }
    private static string ScalarString(JsonElement value, CacheDateTable dates)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = JsonDisplayProjection.DecodedString(value);
            return dates.TryGet(text, out var date) ? date.Value.ToString(CultureInfo.InvariantCulture) : text;
        }
        if (value.ValueKind != JsonValueKind.Number) return JsonDisplayProjection.String(new(value));
        var raw = value.GetRawText();
        // JSON BigInteger string casts retain their digits; floating casts use G15.
        return raw.IndexOfAny(['.', 'e', 'E']) < 0 ? raw == "-0" ? "0" : raw
            : value.GetDouble().ToString("G15", CultureInfo.InvariantCulture);
    }

    public static int Integer(JsonDisplayValue value, CacheDateTable dates)
    {
        if (value.Group is not null) throw new FormatException("An array cannot be converted to Int32.");
        var scalar = value.Scalar;
        if (scalar.ValueKind == JsonValueKind.String && dates.TryGet(JsonDisplayProjection.DecodedString(scalar), out _))
            throw new FormatException("DateTime cannot be converted to Int32.");
        return scalar.ValueKind switch
        {
            JsonValueKind.Null => 0,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Number => DataScalars.Integer(new PsdNumber(scalar.GetRawText())),
            JsonValueKind.String => StringInteger(ScalarString(scalar, dates)),
            _ => throw new FormatException("The field cannot be converted to Int32.")
        };
    }

    private static int StringInteger(string text) => IntegerText.Parse(text);

    public static bool Timestamp(JsonDisplayValue value, CacheDateTable dates, out DateTimeOffset timestamp)
    {
        if (value.Group is null && value.Scalar.ValueKind == JsonValueKind.String &&
            dates.TryGet(JsonDisplayProjection.DecodedString(value.Scalar), out var date))
        {
            timestamp = date.Instant;
            return true;
        }
        return DateTimeOffset.TryParse(String(value, dates), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
    }
}

internal sealed class CacheJsonLimitException(string message) : Exception(message);
