using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace ModpackTools.Core.Data;

internal readonly record struct CacheDateValue(DateTime Value, TimeSpan Offset)
{
    public static CacheDateValue Capture(DateTime date) => new(date,
        date.Kind == DateTimeKind.Utc ? TimeSpan.Zero : TimeZoneInfo.Local.GetUtcOffset(date));
    public DateTimeOffset Instant => new(DateTime.SpecifyKind(Value, DateTimeKind.Unspecified), Offset);
    public string Key => Value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) +
        (Value.Kind == DateTimeKind.Utc ? "Z" : Value.Kind == DateTimeKind.Unspecified ? "" :
        (Offset < TimeSpan.Zero ? "-" : "+") + Offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture));
}

internal sealed class CacheDateTable(IReadOnlyDictionary<string, CacheDateValue> values)
{
    public bool TryGet(string value, out CacheDateValue date) => values.TryGetValue(value, out date);
    public CacheDateTable ForRow(JsonElement root)
    {
        var selected = new Dictionary<string, CacheDateValue>(StringComparer.Ordinal);
        var pending = new Stack<JsonElement>();
        pending.Push(root);
        while (pending.TryPop(out var value))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = JsonDisplayProjection.DecodedString(value);
                if (TryGet(text, out var date)) selected[text] = date;
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var child in value.EnumerateArray()) pending.Push(child);
            else if (value.ValueKind == JsonValueKind.Object)
                foreach (var child in value.EnumerateObject()) pending.Push(child.Value);
        }
        return new(selected.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
