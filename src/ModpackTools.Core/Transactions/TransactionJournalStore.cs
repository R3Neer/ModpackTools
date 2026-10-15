using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core.Source;

namespace ModpackTools.Core.Transactions;

internal static class TransactionJournalStore
{
    public static TransactionOwner ReadOwner(string workspace, ProjectPhysicalIdentity project, TransactionLimits limits)
    {
        using var document = Read(Path.Combine(workspace, "owner.json"), limits);
        var root = document.RootElement; Shape(root, "SchemaVersion", "TransactionId", "Project", "Status"); Version(root);
        var owner = new TransactionOwner(Text(root, "TransactionId"), Project(root.GetProperty("Project")), Text(root, "Status"));
        if (owner.TransactionId != Path.GetFileName(workspace) || !Guid.TryParseExact(owner.TransactionId, "N", out _) || owner.Project != project
            || owner.Status is not ("preparing" or "pending" or "rolledback" or "committed" or "retained")) throw Invalid(workspace);
        return owner;
    }

    public static TransactionJournal ReadJournal(string workspace, TransactionOwner owner, TransactionLimits limits)
    {
        using var document = Read(Path.Combine(workspace, "journal.json"), limits);
        var root = document.RootElement; Shape(root, "SchemaVersion", "TransactionId", "Project", "Status", "BeforeFingerprint", "Changes", "CreatedDirectories", "PublicationTemporaries"); Version(root);
        var status = Text(root, "Status"); var fingerprint = Text(root, "BeforeFingerprint");
        if (Text(root, "TransactionId") != owner.TransactionId || Project(root.GetProperty("Project")) != owner.Project || status is not ("pending" or "committed")
            || !Digest(fingerprint, false)) throw Invalid(workspace);
        var changes = ImmutableArray.CreateBuilder<SourceChange>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in root.GetProperty("Changes").EnumerateArray())
        {
            if (changes.Count >= limits.MaximumChanges) throw Limit(workspace);
            Shape(row, "Path", "Before", "After"); var path = Text(row, "Path");
            _ = TransactionFiles.Within(owner.Project.CanonicalRoot, path);
            var before = NullableText(row, "Before"); var after = NullableText(row, "After");
            if (!paths.Add(path) || before == after || before is not null && !Digest(before, true) || after is not null && !Digest(after, true)) throw Invalid(workspace);
            changes.Add(new(path, before, after));
        }
        var directories = ImmutableArray.CreateBuilder<string>(); paths.Clear();
        foreach (var value in root.GetProperty("CreatedDirectories").EnumerateArray())
        {
            if (directories.Count >= (limits.Tree ?? new()).MaximumDirectories) throw Limit(workspace);
            var relative = value.GetString() ?? throw Invalid(workspace); _ = TransactionFiles.Within(owner.Project.CanonicalRoot, relative);
            if (!paths.Add(relative)) throw Invalid(workspace); directories.Add(relative);
        }
        var temporaries = ImmutableArray.CreateBuilder<string>(); var expectedTemporaries = new List<string>();
        for (var index = 0; index < changes.Count; index++)
        {
            if (changes[index].After is not null) expectedTemporaries.Add(TransactionFiles.PublicationRelative(changes[index].Path, owner.TransactionId, index, false));
            if (changes[index].Before is not null) expectedTemporaries.Add(TransactionFiles.PublicationRelative(changes[index].Path, owner.TransactionId, index, true));
        }
        foreach (var value in root.GetProperty("PublicationTemporaries").EnumerateArray())
        {
            if (temporaries.Count >= expectedTemporaries.Count) throw Invalid(workspace);
            var relative = value.GetString() ?? throw Invalid(workspace); _ = TransactionFiles.Within(owner.Project.CanonicalRoot, relative);
            if (relative != expectedTemporaries[temporaries.Count]) throw Invalid(workspace); temporaries.Add(relative);
        }
        if (temporaries.Count != expectedTemporaries.Count) throw Invalid(workspace);
        return new(owner, fingerprint, changes.ToImmutable(), directories.ToImmutable(), temporaries.ToImmutable(), status);
    }

    public static void WriteOwner(string workspace, TransactionOwner owner, TransactionLimits limits) => Write(Path.Combine(workspace, "owner.json"), limits, writer =>
    {
        Header(writer, owner, owner.Status); writer.WriteEndObject();
    });
    public static void WriteJournal(string workspace, TransactionJournal journal, TransactionLimits limits) => Write(Path.Combine(workspace, "journal.json"), limits, writer =>
    {
        Header(writer, journal.Owner, journal.Status); writer.WriteString("BeforeFingerprint", journal.BeforeFingerprint); writer.WriteStartArray("Changes");
        foreach (var change in journal.Changes)
        {
            writer.WriteStartObject(); writer.WriteString("Path", change.Path); writer.WriteString("Before", change.Before); writer.WriteString("After", change.After); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteStartArray("CreatedDirectories"); foreach (var directory in journal.CreatedDirectories) writer.WriteStringValue(directory);
        writer.WriteEndArray(); writer.WriteStartArray("PublicationTemporaries"); foreach (var temporary in journal.PublicationTemporaries) writer.WriteStringValue(temporary);
        writer.WriteEndArray(); writer.WriteEndObject();
    });

    private static void Header(Utf8JsonWriter writer, TransactionOwner owner, string status)
    {
        writer.WriteStartObject(); writer.WriteNumber("SchemaVersion", 1); writer.WriteString("TransactionId", owner.TransactionId);
        writer.WriteStartObject("Project"); writer.WriteString("CanonicalRoot", owner.Project.CanonicalRoot); writer.WriteString("FileIdentity", owner.Project.FileIdentity); writer.WriteEndObject();
        writer.WriteString("Status", status);
    }
    private static ProjectPhysicalIdentity Project(JsonElement root)
    {
        Shape(root, "CanonicalRoot", "FileIdentity"); var identity = Text(root, "FileIdentity");
        if (identity.Length != 48 || identity.Any(character => !char.IsAsciiHexDigit(character))) throw Invalid(null);
        return new(Text(root, "CanonicalRoot"), identity);
    }
    private static JsonDocument Read(string path, TransactionLimits limits)
    {
        WindowsProjectIdentity.GuardAncestors(path);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = file.Read(buffer)) != 0)
            { if (read > limits.MaximumJournalBytes - bytes.Length) throw Limit(path); bytes.Write(buffer, 0, read); }
            return JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)), new() { MaxDepth = 8 });
        }
        catch (JsonException error) { throw Invalid(path, error.Message); }
    }
    private static void Write(string path, TransactionLimits limits, Action<Utf8JsonWriter> write)
    {
        WindowsProjectIdentity.GuardAncestors(path); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; var owned = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                using (var bounded = new BoundedOutput(file, limits.MaximumJournalBytes, path))
                using (var writer = new Utf8JsonWriter(bounded)) { write(writer); writer.Flush(); }
                file.Flush(flushToDisk: true);
            }
            WindowsProjectIdentity.GuardAncestors(path); File.Move(temporary, path, overwrite: true); owned = false;
        }
        finally { if (owned) File.Delete(temporary); }
    }
    private static void Shape(JsonElement root, params string[] fields)
    {
        if (root.ValueKind != JsonValueKind.Object) throw Invalid(null);
        var expected = fields.ToHashSet(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject()) if (!expected.Remove(property.Name)) throw Invalid(null);
        if (expected.Count != 0) throw Invalid(null);
    }
    private static void Version(JsonElement root) { if (!root.GetProperty("SchemaVersion").TryGetInt32(out var value) || value != 1) throw Invalid(null); }
    private static string Text(JsonElement row, string field) => row.GetProperty(field).ValueKind == JsonValueKind.String ? row.GetProperty(field).GetString()! : throw Invalid(null);
    private static string? NullableText(JsonElement row, string field) => row.GetProperty(field).ValueKind == JsonValueKind.Null ? null : Text(row, field);
    private static bool Digest(string value, bool upper) => value.Length == 64 && value.All(character => char.IsAsciiDigit(character) || (upper ? character is >= 'A' and <= 'F' : character is >= 'a' and <= 'f'));
    internal static DomainException Invalid(string? path, string? details = null) => WindowsProjectIdentity.Error("InvalidJournal", "Transaction ownership or journal data is invalid", path, details);
    private static DomainException Limit(string path) => new("Transaction.JournalLimit", "Transaction journal exceeds its resource limits", "use bounded transaction quotas", "LimitsExceeded", path);

    private sealed class BoundedOutput(Stream output, long maximum, string path) : Stream
    {
        private long bytes;
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { if (buffer.Length > maximum - bytes) throw Limit(path); output.Write(buffer); bytes += buffer.Length; }
        public override void Flush() => output.Flush();
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => bytes; public override long Position { get => bytes; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
