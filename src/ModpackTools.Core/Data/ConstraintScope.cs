using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;
using Newtonsoft.Json;

namespace ModpackTools.Core.Data;

internal sealed class ConstraintScope(RootManifestLimits limits, byte loweringProfile = 0)
{
    private readonly Dictionary<object, VersionConstraint> constraints = new(ReferenceEqualityComparer.Instance);
    private int retained;
    public VersionConstraint Json(OrderedManifestValue value, VersionRange? semantic = null) => Get(value,
        () => semantic ?? OrderedManifestProjection.Range(value), () => OrderedManifestProjection.RangeDisplay(value));
    public VersionConstraint Toml(TomlDeclarationValue value) => Get(value,
        () => TomlDeclarationProjection.Range(value), () => TomlDeclarationProjection.RangeDisplay(value));
    private VersionConstraint Get(object value, Func<VersionRange> range, Func<string> display)
    {
        if (constraints.TryGetValue(value, out var existing)) return existing;
        var semantic = range(); var text = display();
        var legacy = Wire(value, false, out var enums);
        var publicJson = enums ? Wire(value, true, out _) : legacy;
        using var fingerprint = new Fingerprint(loweringProfile); fingerprint.Value(value);
        var result = VersionConstraint.Captured(semantic, text, legacy, publicJson, fingerprint.Finish());
        constraints.Add(value, result); return result;
    }
    private string Wire(object value, bool publicProfile, out bool enums)
    {
        using var output = new BoundedWriter(limits.MaximumConstraintWireCharacters, count =>
        {
            if (count > limits.MaximumAggregateConstraintWireCharacters - retained) throw new ManifestLimitException(nameof(limits.MaximumAggregateConstraintWireCharacters));
            retained += count;
        });
        using var writer = new JsonTextWriter(output) { Formatting = Formatting.None, Culture = CultureInfo.InvariantCulture, CloseOutput = false, AutoCompleteOnClose = false };
        var containsEnums = false;
        Write(value); writer.Flush(); enums = containsEnums; return output.ToString();
        void Write(object item)
        {
            switch (item)
            {
                case ManifestNull or ManifestOmitted or TomlMissing: writer.WriteNull(); break;
                case ManifestString text: writer.WriteValue(text.Value); break;
                case TomlText text: writer.WriteValue(text.Value); break;
                case ManifestInteger integer: writer.WriteValue(integer.Value); break;
                case TomlInteger integer: writer.WriteValue(integer.Value); break;
                case ManifestBigInteger integer: writer.WriteRawValue(integer.Value.ToString(CultureInfo.InvariantCulture)); break;
                case ManifestFloat number: writer.WriteValue(number.Value); break;
                case TomlFloat number: writer.WriteValue(number.Value); break;
                case ManifestBoolean boolean: writer.WriteValue(boolean.Value); break;
                case TomlBoolean boolean: writer.WriteValue(boolean.Value); break;
                case ManifestDate date: writer.WriteValue(date.Value); break;
                case TomlDateText date:
                    containsEnums = true; writer.WriteStartObject(); writer.WritePropertyName("DateTime"); writer.WriteValue(date.DateTime);
                    writer.WritePropertyName("SecondPrecision"); writer.WriteValue(date.SecondPrecision); writer.WritePropertyName("Kind");
                    if (publicProfile) writer.WriteValue(date.KindName); else writer.WriteValue(date.Kind);
                    writer.WriteEndObject(); break;
                case ManifestArray array:
                    writer.WriteStartArray(); foreach (var child in array.Values) Write(child); writer.WriteEndArray(); break;
                case ManifestMap map:
                    writer.WriteStartObject(); foreach (var entry in map.Entries) { writer.WritePropertyName(entry.Key); Write(entry.Value); }
                    writer.WriteEndObject(); break;
                case TomlSequence array:
                    writer.WriteStartArray(); foreach (var child in array.Items) Write(child); writer.WriteEndArray(); break;
                case TomlFields table:
                    writer.WriteStartArray(); foreach (var entry in table.Entries) Entry(entry.Key, entry.Value); writer.WriteEndArray(); break;
                case TomlEntry entry: Entry(entry.Key, entry.Value); break;
                default: throw new InvalidOperationException("Unsupported owned constraint value.");
            }
        }
        void Entry(string key, TomlDeclarationValue item)
        { writer.WriteStartObject(); writer.WritePropertyName("Key"); writer.WriteValue(key); writer.WritePropertyName("Value"); Write(item); writer.WriteEndObject(); }
    }
    private sealed class BoundedWriter(int maximum, Action<int> charge) : TextWriter
    {
        private readonly StringBuilder text = new();
        public override Encoding Encoding => Encoding.UTF8;
        private void Guard(int count)
        {
            if (count > maximum - text.Length) throw new ManifestLimitException("MaximumConstraintWireCharacters");
            charge(count);
        }
        public override void Write(char value) { Guard(1); text.Append(value); }
        public override void Write(char[] buffer, int index, int count) { Guard(count); text.Append(buffer, index, count); }
        public override void Write(string? value) { if (value is not null) { Guard(value.Length); text.Append(value); } }
        public override void Write(ReadOnlySpan<char> buffer) { Guard(buffer.Length); text.Append(buffer); }
        public override string ToString() => text.ToString();
    }
    private sealed class Fingerprint : IDisposable
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public Fingerprint(byte profile) { hash.AppendData("ModpackTools/version-constraint/evidence/v1\0"u8); Tag(profile); }
        private void Tag(byte tag) { Span<byte> bytes = stackalloc byte[1]; bytes[0] = tag; hash.AppendData(bytes); }
        private void I32(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes); }
        private void I64(long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }
        private void Text(string value)
        {
            I32(value.Length); Span<byte> bytes = stackalloc byte[1024];
            for (var offset = 0; offset < value.Length; offset += 512)
            {
                var count = Math.Min(512, value.Length - offset);
                for (var index = 0; index < count; index++) BinaryPrimitives.WriteUInt16BigEndian(bytes[(index * 2)..], value[offset + index]);
                hash.AppendData(bytes[..(count * 2)]);
            }
        }
        public void Value(object value)
        {
            switch (value)
            {
                case ManifestNull or TomlMissing: Tag(0); break;
                case ManifestOmitted: Tag(1); break;
                case ManifestString text: Tag(2); Text(text.Value); break;
                case TomlText text: Tag(2); Text(text.Value); break;
                case ManifestInteger integer: Tag(3); I64(integer.Value); break;
                case TomlInteger integer: Tag(3); I64(integer.Value); break;
                case ManifestBigInteger integer:
                    Tag(4); var magnitude = integer.Value.ToByteArray(isUnsigned: false, isBigEndian: true); I32(magnitude.Length); hash.AppendData(magnitude); break;
                case ManifestFloat number: Tag(5); I64(BitConverter.DoubleToInt64Bits(number.Value)); break;
                case TomlFloat number: Tag(5); I64(BitConverter.DoubleToInt64Bits(number.Value)); break;
                case ManifestBoolean boolean: Tag(6); Tag(boolean.Value ? (byte)1 : (byte)0); break;
                case TomlBoolean boolean: Tag(6); Tag(boolean.Value ? (byte)1 : (byte)0); break;
                case ManifestDate date:
                    Tag(7); I64(date.Value.Ticks); I32((int)date.Value.Kind);
                    I64(date.Value.Kind == DateTimeKind.Local ? new DateTimeOffset(date.Value).Offset.Ticks : 0); break;
                case ManifestArray array: Tag(8); I32(array.Values.Length); foreach (var child in array.Values) Value(child); break;
                case ManifestMap map:
                    Tag(9); I32(map.Entries.Length); foreach (var entry in map.Entries) { Text(entry.Key); Value(entry.Value); }
                    break;
                case TomlDateText date:
                    Tag(10); I64(date.DateTime.Ticks); I64(date.DateTime.Offset.Ticks); I32(date.SecondPrecision); I32(date.Kind); Text(date.KindName); break;
                case TomlSequence array:
                    Tag(11); Tag((byte)((array.TableArray ? 1 : 0) | (array.PipelineArray ? 2 : 0))); I32(array.Items.Length); foreach (var child in array.Items) Value(child); break;
                case TomlFields table:
                    Tag(12); I32(table.Entries.Length); foreach (var entry in table.Entries) { Text(entry.Key); Value(entry.Value); }
                    break;
                case TomlEntry entry: Tag(13); Text(entry.Key); Value(entry.Value); break;
                default: throw new InvalidOperationException("Unsupported owned constraint fingerprint value.");
            }
        }
        public string Finish() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        public void Dispose() => hash.Dispose();
    }
}
