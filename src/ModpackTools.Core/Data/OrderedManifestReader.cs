using System.Globalization;
using System.Numerics;
using ModpackTools.Core.Artifacts;
using Newtonsoft.Json;

namespace ModpackTools.Core.Data;

internal sealed class ManifestLimitException(string property) : Exception(property + " exceeded.")
{ public string Property { get; } = property; }

internal static class OrderedManifestReader
{
    public static OrderedManifestValue Read(string text, RootManifestLimits limits)
    {
        using var input = new StringReader(text);
        using var guarded = new TokenLengthReader(input, limits.MaximumTokenCharacters);
        using var reader = new JsonTextReader(guarded)
        {
            MaxDepth = limits.MaximumJsonDepth,
            DateParseHandling = DateParseHandling.DateTime,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            FloatParseHandling = FloatParseHandling.Double,
            Culture = CultureInfo.InvariantCulture,
            CloseInput = false
        };
        var frames = new Stack<Frame>();
        OrderedManifestValue? root = null;
        var nodes = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.Comment) continue;
            if (reader.TokenType == JsonToken.PropertyName)
            {
                if (frames.Count == 0 || frames.Peek().Array is not null) throw new FormatException("Unexpected manifest property.");
                frames.Peek().Property = (string)reader.Value!;
                continue;
            }
            if (reader.TokenType is JsonToken.EndArray or JsonToken.EndObject)
            {
                if (!frames.TryPop(out var frame)) throw new FormatException("Unexpected manifest container end.");
                Add(frame.Seal());
                continue;
            }
            if (++nodes > limits.MaximumNodes) throw new ManifestLimitException(nameof(limits.MaximumNodes));
            switch (reader.TokenType)
            {
                case JsonToken.StartArray: frames.Push(new(true)); break;
                case JsonToken.StartObject: frames.Push(new(false)); break;
                case JsonToken.String: Add(new ManifestString((string)reader.Value!)); break;
                case JsonToken.Integer:
                    Add(reader.Value is BigInteger big ? new ManifestBigInteger(big) : new ManifestInteger((long)reader.Value!)); break;
                case JsonToken.Float: Add(new ManifestFloat((double)reader.Value!)); break;
                case JsonToken.Boolean: Add(new ManifestBoolean((bool)reader.Value!)); break;
                case JsonToken.Date: Add(new ManifestDate((DateTime)reader.Value!)); break;
                case JsonToken.Null or JsonToken.Undefined: Add(ManifestNull.Value); break;
                case JsonToken.StartConstructor:
                    if (frames.Count == 0) throw new FormatException("Root JSON constructors are unsupported.");
                    if ((string?)reader.Value == "Date") Add(new ManifestDate(LegacyDateConstructor.Read(reader)));
                    else { SkipConstructor(); Add(ManifestOmitted.Value); }
                    break;
                default: throw new FormatException("Unsupported manifest token.");
            }
        }
        if (frames.Count != 0) throw new FormatException("Unexpected end of manifest JSON.");
        // Match assignment of the converter's root pipeline, not nested flattening.
        return root is ManifestArray array ? array.Values.Length switch
        { 0 => ManifestNull.Value, 1 => array.Values[0], _ => root } : root ?? ManifestNull.Value;

        void Add(OrderedManifestValue value)
        {
            if (frames.Count != 0) { frames.Peek().Add(value); return; }
            if (root is not null) throw new FormatException("Additional root JSON content.");
            root = value;
        }
        void SkipConstructor()
        {
            var depth = reader.Depth;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndConstructor && reader.Depth == depth) return;
                if (++nodes > limits.MaximumNodes) throw new ManifestLimitException(nameof(limits.MaximumNodes));
                if (reader.TokenType == JsonToken.StartConstructor && (string?)reader.Value == "Date") _ = LegacyDateConstructor.Read(reader);
            }
            throw new FormatException("Unclosed manifest constructor.");
        }
    }
    private sealed class Frame
    {
        public List<OrderedManifestValue>? Array { get; }
        private readonly Dictionary<string, (OrderedManifestValue Value, int Position)>? fields;
        private int position;
        public string? Property { get; set; }
        public Frame(bool array)
        { if (array) Array = []; else fields = new(StringComparer.Ordinal); }
        public void Add(OrderedManifestValue value)
        {
            if (Array is not null) { if (value is not ManifestOmitted) Array.Add(value); return; }
            var name = Property ?? throw new FormatException("Manifest object value has no property name.");
            fields![name] = (value, position++);
            Property = null;
        }
        public OrderedManifestValue Seal() => Array is { } array ? new ManifestArray([.. array]) : new ManifestMap(
            [.. fields!.Where(entry => entry.Value.Value is not ManifestOmitted).OrderBy(entry => entry.Value.Position)
                .Select(entry => KeyValuePair.Create(entry.Key, entry.Value.Value))]);
    }
}
