using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;
using Newtonsoft.Json;

namespace ModpackTools.Core.Graph;

internal sealed record GraphFact(string Scope, string Target, bool Present, ImmutableArray<string?> Versions, ImmutableArray<string?> LegacyVersions);
internal sealed class GraphTextBudget(GraphReportLimits limits)
{
    private int identity, messages, factVersions;
    public void Identity(int count) { GraphResource.Guard(count > limits.MaximumIdentityCharacters - identity, nameof(limits.MaximumIdentityCharacters)); identity += count; }
    public void Message(int count) { GraphResource.Guard(count > limits.MaximumMessageCharacters - messages, nameof(limits.MaximumMessageCharacters)); messages += count; }
    public void FactVersions(int count) { GraphResource.Guard(count > limits.MaximumFactVersions - factVersions, nameof(limits.MaximumFactVersions)); factVersions += count; }
}
internal sealed class GraphIdentity(GraphTextBudget budget)
{
    private readonly Dictionary<VersionConstraint, int> depths = new(ReferenceEqualityComparer.Instance);
    public string Modern(GraphNode? owner, string side, byte kind, ManifestRequirement? requirement,
        string? payload, ImmutableArray<GraphFact> facts, bool includeFacts)
    {
        using var frame = new Frame(budget);
        frame.Text(owner?.Id ?? ""); frame.Text(owner?.VersionId ?? ""); frame.Text(side); frame.Byte(kind);
        if (requirement is not null) Requirement(requirement); else frame.Text(payload!);
        frame.Byte(includeFacts ? (byte)1 : (byte)0); frame.Number(facts.Length);
        foreach (var fact in facts)
        {
            frame.Text(fact.Scope); frame.Text(fact.Target); frame.Byte(fact.Present ? (byte)1 : (byte)0);
            budget.FactVersions(fact.Versions.Length);
            frame.Number(fact.Versions.Length);
            foreach (var version in fact.Versions)
            { frame.Byte(version is null ? (byte)0 : (byte)1); if (version is not null) frame.Text(version); }
        }
        return frame.Finish();
        void Requirement(ManifestRequirement value)
        {
            frame.Text(value.Target); frame.Text(value.Kind); frame.Text(value.Side); frame.Text(value.Scope);
            frame.Text(value.Source); frame.Text(value.SuggestedVersionId); frame.Byte(value.Scheme == VersionScheme.Maven ? (byte)1 : (byte)0);
            frame.Raw(Convert.FromHexString(value.Constraint.TypedFingerprint));
            var any = value.Any.IsDefault ? ImmutableArray<ManifestRequirement>.Empty : value.Any;
            var all = value.All.IsDefault ? ImmutableArray<ManifestRequirement>.Empty : value.All;
            frame.Number(any.Length); foreach (var child in any) Requirement(child);
            frame.Number(all.Length); foreach (var child in all) Requirement(child);
            frame.Byte(value.Unless is null ? (byte)0 : (byte)1); if (value.Unless is not null) Requirement(value.Unless);
        }
    }
    public string? Auxiliary(GraphNode? owner, string side, ManifestRequirement? requirement, string? warning,
        string? runtimeOverride, ImmutableArray<GraphFact> facts, bool includeFacts)
    {
        if (runtimeOverride is not null) return Legacy(output => output.Write(runtimeOverride));
        if (requirement is not null && !Safe(requirement, 0)) return null;
        var initial = Legacy(output =>
        {
            output.Write(owner!.Id); output.Write('|'); output.Write(owner.VersionId); output.Write('|'); output.Write(side); output.Write('|');
            using var writer = new JsonTextWriter(output) { Formatting = Formatting.None, Culture = CultureInfo.InvariantCulture, CloseOutput = false, AutoCompleteOnClose = false };
            if (requirement is null) writer.WriteValue(warning); else WriteRequirement(writer, requirement);
            writer.Flush();
        });
        if (!includeFacts) return initial;
        return Legacy(output =>
        {
            output.Write(initial); output.Write('|');
            var first = true;
            foreach (var fact in facts)
            {
                budget.FactVersions(fact.LegacyVersions.Length);
                foreach (var version in fact.LegacyVersions)
                { if (!first) output.Write(','); first = false; output.Write(version); }
            }
        });
    }
    private bool Safe(ManifestRequirement requirement, int depth)
    {
        // Conservatively stay strictly below the old serializer's Depth30.
        if (depth + 1 >= 30 || Depth(requirement.Constraint) + depth + 1 >= 30) return false;
        if (!requirement.Any.IsDefault) foreach (var child in requirement.Any) if (!Safe(child, depth + 2)) return false;
        if (!requirement.All.IsDefault) foreach (var child in requirement.All) if (!Safe(child, depth + 2)) return false;
        return requirement.Unless is null || Safe(requirement.Unless, depth + 1);
    }
    private int Depth(VersionConstraint constraint)
    {
        if (depths.TryGetValue(constraint, out var existing)) return existing;
        using var input = new StringReader(constraint.LegacyJson);
        using var reader = new JsonTextReader(input) { MaxDepth = null, DateParseHandling = DateParseHandling.None };
        var depth = 0;
        while (reader.Read()) { depth = Math.Max(depth, reader.Depth); if (depth >= 30) break; }
        depths.Add(constraint, depth); return depth;
    }
    private string Legacy(Action<TextWriter> write)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new HashStream(hash);
        using var utf8 = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
        using var output = new CountingWriter(utf8, budget);
        write(output); output.Flush(); return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static void WriteRequirement(JsonTextWriter writer, ManifestRequirement requirement)
    {
        writer.WriteStartObject();
        void Text(string key, string value) { writer.WritePropertyName(key); writer.WriteValue(value); }
        Text("Target", requirement.Target); writer.WritePropertyName("Range"); writer.WriteRawValue(requirement.Constraint.LegacyJson);
        Text("Kind", requirement.Kind); Text("Side", requirement.Side); Text("Scope", requirement.Scope);
        writer.WritePropertyName("Maven"); writer.WriteValue(requirement.Scheme == VersionScheme.Maven);
        Text("Source", requirement.Source); Text("SuggestedVersionId", requirement.SuggestedVersionId);
        writer.WritePropertyName("Any"); writer.WriteStartArray(); if (!requirement.Any.IsDefault) foreach (var child in requirement.Any) WriteRequirement(writer, child); writer.WriteEndArray();
        writer.WritePropertyName("All"); writer.WriteStartArray(); if (!requirement.All.IsDefault) foreach (var child in requirement.All) WriteRequirement(writer, child); writer.WriteEndArray();
        writer.WritePropertyName("Unless"); if (requirement.Unless is null) writer.WriteNull(); else WriteRequirement(writer, requirement.Unless);
        writer.WriteEndObject();
    }
    private sealed class CountingWriter(TextWriter output, GraphTextBudget budget) : TextWriter
    {
        public override Encoding Encoding => output.Encoding;
        public override void Write(char value) { budget.Identity(1); output.Write(value); }
        public override void Write(string? value) { if (value is not null) { budget.Identity(value.Length); output.Write(value); } }
        public override void Write(char[] buffer, int index, int count) { budget.Identity(count); output.Write(buffer, index, count); }
        public override void Write(ReadOnlySpan<char> value) { budget.Identity(value.Length); output.Write(value); }
        public override void Flush() => output.Flush();
    }
    private sealed class HashStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private sealed class Frame : IDisposable
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly GraphTextBudget budget;
        public Frame(GraphTextBudget budget) { this.budget = budget; hash.AppendData("ModpackTools/graph-issue/v2\0"u8); }
        public void Byte(byte value) { Span<byte> bytes = stackalloc byte[1]; bytes[0] = value; hash.AppendData(bytes); }
        public void Number(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes); }
        public void Text(string value)
        {
            budget.Identity(value.Length); Number(value.Length); Span<byte> bytes = stackalloc byte[1024];
            for (var offset = 0; offset < value.Length; offset += 512)
            {
                var count = Math.Min(512, value.Length - offset);
                for (var index = 0; index < count; index++) BinaryPrimitives.WriteUInt16BigEndian(bytes[(index * 2)..], value[offset + index]);
                hash.AppendData(bytes[..(count * 2)]);
            }
        }
        public void Raw(ReadOnlySpan<byte> bytes) => hash.AppendData(bytes);
        public string Finish() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        public void Dispose() => hash.Dispose();
    }
}
