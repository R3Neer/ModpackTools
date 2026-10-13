using System.Globalization;
using System.Text.Json;

namespace ModpackTools.Core.Data;

internal readonly record struct JsonDisplayValue(JsonElement Scalar, JsonElement[]? Group = null);

// Compatibility at the data boundary only. Domain consumers receive typed strings,
// never PowerShell objects, member adapters or an embedded shell runtime.
internal static class JsonDisplayProjection
{
    public static void Validate(JsonElement root)
    {
        var pending = new Stack<JsonElement>();
        pending.Push(root);
        while (pending.TryPop(out var element))
        {
            if (element.ValueKind == JsonValueKind.Array)
                foreach (var child in element.EnumerateArray()) pending.Push(child);
            else if (element.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in Properties(element))
                {
                    if (key.Length == 0 || !keys.Add(key))
                        throw new FormatException("The JSON contains an empty or case-conflicting property name.");
                    pending.Push(value);
                }
            }
        }
    }

    public static JsonDisplayValue Field(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1) root = root[0];
        var pending = new Stack<JsonElement>();
        var values = new List<JsonElement>();
        pending.Push(root);
        var collection = root.ValueKind == JsonValueKind.Array;
        while (pending.TryPop(out var element))
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                for (var index = element.GetArrayLength() - 1; index >= 0; index--) pending.Push(element[index]);
                continue;
            }
            if (element.ValueKind != JsonValueKind.Object)
                throw new FormatException($"Display property '{name}' is unavailable.");
            var found = false;
            foreach (var (key, value) in Properties(element))
            {
                if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                if (collection && value.ValueKind == JsonValueKind.Array) values.AddRange(value.EnumerateArray());
                else values.Add(value);
                break;
            }
            if (!found) throw new FormatException($"Display property '{name}' is unavailable.");
        }
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
            throw new FormatException($"Display property '{name}' is unavailable.");
        return values.Count == 1 ? new(values[0]) : new(default, values.ToArray());
    }

    public static bool Truthy(JsonDisplayValue value)
    {
        if (value.Group is { } group)
            return group.Length > 1 || group.Length == 1 && Truthy(new(group[0]));
        var element = value.Scalar;
        while (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() == 1) element = element[0];
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => false,
            JsonValueKind.String => DecodedString(element).Length != 0,
            JsonValueKind.Number => NumberTruthy(element),
            JsonValueKind.Array => element.GetArrayLength() != 0,
            _ => true
        };
    }

    public static string String(JsonDisplayValue value) => value.Group is { } group
        ? string.Join(" ", group.Select(NestedString)) : String(value.Scalar);

    private static string String(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => string.Join(" ", value.EnumerateArray().Select(NestedString)),
        JsonValueKind.Object => ObjectString(value),
        _ => ScalarString(value)
    };

    private static string NestedString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => "System.Object[]",
        JsonValueKind.Object => "",
        _ => ScalarString(value)
    };

    private static string ObjectString(JsonElement value)
    {
        var properties = Properties(value);
        return properties.Count == 0 ? "" : "@{" + string.Join("; ", properties.Select(pair => pair.Key + "=" + NestedString(pair.Value))) + "}";
    }

    private static string ScalarString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        JsonValueKind.Number => NumberString(value),
        JsonValueKind.String => DateString(DecodedString(value)),
        _ => throw new FormatException("Unsupported display scalar.")
    };

    private static string NumberString(JsonElement value)
    {
        var text = value.GetRawText();
        return text.IndexOfAny(['.', 'e', 'E']) < 0
            ? text == "-0" ? "0" : text
            : value.GetDouble().ToString(CultureInfo.InvariantCulture);
    }

    private static bool NumberTruthy(JsonElement value)
    {
        var text = value.GetRawText();
        // Validated JSON integers are already canonical except negative zero.
        // Preserve arbitrarily large values without constructing a BigInteger.
        return text.IndexOfAny(['.', 'e', 'E']) < 0 ? text is not ("0" or "-0") : value.GetDouble() != 0;
    }

    private static string DateString(string value) => value.Length is >= 19 and <= 40 && value[10] == 'T' &&
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
        ? date.ToString(CultureInfo.InvariantCulture) : value;

    internal static Dictionary<string, JsonElement> Properties(JsonElement value)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            string name;
            try { name = property.Name; }
            catch (InvalidOperationException) { name = DecodeRawString(property.ToString()); }
            result[name] = property.Value;
        }
        return result;
    }

    internal static string DecodedString(JsonElement value)
    {
        try { return value.GetString()!; }
        catch (InvalidOperationException) { return DecodeRawString(value.GetRawText()); }
    }

    // The legacy JSON reader replaces isolated UTF16 surrogate escapes before
    // property identity/duplicate checks, not only when presenting the result.
    // Request protocol decoding remains strict and does not use this projection.
    private static string DecodeRawString(string raw)
    {
        var result = new System.Text.StringBuilder();
        for (var index = 1; index < raw.Length; index++)
        {
            var character = raw[index];
            if (character == '"')
            {
                var decoded = result.ToString();
                return System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(decoded));
            }
            if (character != '\\') { result.Append(character); continue; }
            character = raw[++index];
            if (character == 'u')
            {
                result.Append((char)ushort.Parse(raw.AsSpan(index + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                index += 4;
            }
            else result.Append(character switch { 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => character });
        }
        throw new FormatException("The JSON string is incomplete.");
    }
}
