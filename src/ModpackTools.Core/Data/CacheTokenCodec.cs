using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json;

namespace ModpackTools.Core.Data;

internal sealed record DecodedCache(JsonDocument Document, CacheDateTable Dates);

// Only the legacy token APIs are used. The temporary BCL graph is private to
// decoding; neither it nor Newtonsoft object models enter domain contracts.
internal static class CacheTokenCodec
{
    public static DecodedCache? Read(TextReader input, int maximumDepth, int maximumCharacters,
        int maximumTokenCharacters, int maximumNodes)
    {
        using var guarded = new TokenLengthReader(input, maximumTokenCharacters);
        using var reader = new JsonTextReader(guarded)
        {
            MaxDepth = maximumDepth,
            DateParseHandling = DateParseHandling.DateTime,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            FloatParseHandling = FloatParseHandling.Double,
            Culture = CultureInfo.InvariantCulture,
            CloseInput = false
        };
        var builder = new Builder(maximumNodes);
        while (reader.Read()) builder.Accept(reader);
        builder.Complete();
        if (!builder.Started) return null;
        using var bytes = new MemoryStream();
        using (var output = new NormalizedOutput(bytes, maximumCharacters))
        using (var writer = new Utf8JsonWriter(output, new() { MaxDepth = maximumDepth }))
            builder.Write(writer);
        var document = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)),
            new JsonDocumentOptions { MaxDepth = maximumDepth });
        return new(document, builder.Dates);
    }

    private sealed class Frame(JsonNode node)
    {
        public JsonNode Node { get; } = node;
        public string? Property { get; set; }
        public HashSet<string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Builder(int maximumNodes)
    {
        private readonly Stack<Frame> frames = new();
        private readonly Dictionary<JsonObject, HashSet<string>> ignored = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<JsonValue, CacheDateValue> typedDates = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, CacheDateValue> visibleDates = new(StringComparer.Ordinal);
        private JsonNode? root;
        private int nodes;
        public bool Started { get; private set; }
        public CacheDateTable Dates => new(visibleDates.ToFrozenDictionary(StringComparer.Ordinal));
        public void Complete()
        {
            if (frames.Count != 0) throw new FormatException("Unexpected end of JSON content.");
        }

        public void Accept(JsonTextReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Comment: return;
                case JsonToken.PropertyName:
                    var frame = frames.Peek();
                    var name = (string)reader.Value!;
                    if (frame.Node is not JsonObject objectNode || name.Length == 0 ||
                        !frame.Keys.Add(name) && !objectNode.ContainsKey(name))
                        throw new FormatException("The JSON contains an empty or case-conflicting property name.");
                    frame.Property = name;
                    return;
                case JsonToken.EndObject or JsonToken.EndArray:
                    frames.Pop();
                    return;
                case JsonToken.StartConstructor:
                    if (frames.Count == 0) throw new FormatException("Root constructors are unsupported by the legacy cache reader.");
                    if ((string?)reader.Value == "Date") AddDate(ReadDateConstructor(reader));
                    else { SkipConstructor(reader); Omit(); }
                    return;
            }
            Count();
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    var obj = new JsonObject();
                    Add(obj); frames.Push(new(obj));
                    break;
                case JsonToken.StartArray:
                    var array = new JsonArray();
                    Add(array); frames.Push(new(array));
                    break;
                case JsonToken.String: Add(JsonValue.Create((string?)reader.Value)); break;
                case JsonToken.Date: AddDate((DateTime)reader.Value!, count: false); break;
                case JsonToken.Boolean: Add(JsonValue.Create((bool?)(bool)reader.Value!)); break;
                case JsonToken.Null or JsonToken.Undefined: Add(null); break;
                case JsonToken.Integer:
                    Add(reader.Value switch
                    {
                        long integer => JsonValue.Create((long?)integer),
                        BigInteger big => JsonNode.Parse(big.ToString(CultureInfo.InvariantCulture)),
                        _ => throw new FormatException("Unexpected integer token.")
                    });
                    break;
                case JsonToken.Float:
                    var number = (double)reader.Value!;
                    Add(double.IsFinite(number) ? JsonValue.Create((double?)number)
                        : JsonValue.Create(number.ToString(CultureInfo.InvariantCulture)));
                    break;
                default: throw new FormatException("Unsupported cache token: " + reader.TokenType);
            }
        }

        private void Count()
        {
            if (nodes == maximumNodes) throw new CacheJsonLimitException("JSON value/container count exceeds the reader quota.");
            nodes++;
        }
        private void Add(JsonNode? value)
        {
            if (frames.Count == 0)
            {
                if (Started) throw new FormatException("Additional root JSON content.");
                root = value; Started = true;
                return;
            }
            var frame = frames.Peek();
            if (frame.Node is JsonArray array) array.Add(value);
            else if (frame.Node is JsonObject obj)
            {
                var key = frame.Property ?? throw new FormatException("Object value without a property name.");
                obj.Remove(key);
                obj.Add(key, value);
                if (ignored.TryGetValue(obj, out var keys)) keys.Remove(key);
                frame.Property = null;
            }
        }
        private void Omit()
        {
            var frame = frames.Peek();
            if (frame.Node is not JsonObject obj) return;
            var key = frame.Property ?? throw new FormatException("Constructor without a property name.");
            // Retain an ignored private slot for name validation. The legacy
            // converter moves an exact duplicate to its last occurrence order.
            obj.Remove(key);
            obj.Add(key, null);
            if (!ignored.TryGetValue(obj, out var keys)) ignored[obj] = keys = new(StringComparer.Ordinal);
            keys.Add(key);
            frame.Property = null;
        }
        private void AddDate(DateTime date, bool count = true)
        {
            if (count) Count();
            var value = JsonValue.Create((DateTime?)date)!;
            typedDates[value] = CacheDateValue.Capture(date);
            Add(value);
        }

        private static DateTime ReadDateConstructor(JsonTextReader reader)
        {
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using var writer = new JsonTextWriter(text) { DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind };
            using var arguments = new DateConstructorReader(reader);
            writer.WriteToken(arguments, writeChildren: false);
            writer.Flush();
            using var parsed = JsonDocument.Parse(text.ToString());
            return parsed.RootElement.GetDateTime();
        }

        // The library accumulates constructor integers before checking Count.
        // Reject the intrinsically invalid eighth argument before its List.Add;
        // retain the library's actual date conversion rather than copying it.
        private sealed class DateConstructorReader : JsonReader
        {
            private readonly JsonTextReader source;
            private int count;
            public DateConstructorReader(JsonTextReader source)
            {
                this.source = source;
                SetToken(JsonToken.StartConstructor, "Date");
            }
            public override bool Read()
            {
                if (!source.Read()) return false;
                if (source.TokenType == JsonToken.Integer && ++count > 7)
                    throw new FormatException("A Date constructor accepts at most seven arguments.");
                SetToken(source.TokenType, source.Value);
                return true;
            }
        }
        private static void SkipConstructor(JsonTextReader reader)
        {
            var depth = reader.Depth;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.StartConstructor && (string?)reader.Value == "Date")
                    _ = ReadDateConstructor(reader);
                else if (reader.TokenType == JsonToken.EndConstructor && reader.Depth == depth) return;
            }
            throw new FormatException("Unclosed JSON constructor.");
        }

        public void Write(Utf8JsonWriter writer) => Write(writer, root);
        private void Write(Utf8JsonWriter writer, JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                writer.WriteStartObject();
                ignored.TryGetValue(obj, out var skipped);
                foreach (var (key, child) in obj)
                {
                    if (skipped?.Contains(key) == true) continue;
                    writer.WritePropertyName(key);
                    Flush(writer);
                    Write(writer, child);
                }
                writer.WriteEndObject();
            }
            else if (node is JsonArray array)
            {
                writer.WriteStartArray();
                foreach (var child in array) Write(writer, child);
                writer.WriteEndArray();
            }
            else if (node is JsonValue value)
            {
                if (typedDates.TryGetValue(value, out var date))
                {
                    visibleDates[date.Key] = date;
                    writer.WriteStringValue(date.Key);
                }
                else if (value.TryGetValue<JsonElement>(out var raw)) raw.WriteTo(writer);
                else if (value.TryGetValue<long>(out var integer)) writer.WriteNumberValue(integer);
                else if (value.TryGetValue<double>(out var number))
                {
                    var text = number.ToString("R", CultureInfo.InvariantCulture);
                    writer.WriteRawValue(text.IndexOfAny(['.', 'e', 'E']) < 0 ? text + ".0" : text);
                }
                else if (value.TryGetValue<bool>(out var flag)) writer.WriteBooleanValue(flag);
                else if (value.TryGetValue<string>(out var text)) writer.WriteStringValue(text);
                else throw new FormatException("Unexpected normalized cache scalar.");
            }
            else writer.WriteNullValue();
            Flush(writer);
        }
        private static void Flush(Utf8JsonWriter writer) { if (writer.BytesPending >= 16 * 1024) writer.Flush(); }
    }

    private sealed class NormalizedOutput(Stream destination, int maximumCharacters) : Stream
    {
        private long written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written;
        public override long Position { get => written; set => throw new NotSupportedException(); }
        public override void Flush() => destination.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > maximumCharacters - written) throw new CacheJsonLimitException("Normalized JSON exceeds the character-work quota.");
            destination.Write(buffer);
            written += buffer.Length;
        }
    }
}
