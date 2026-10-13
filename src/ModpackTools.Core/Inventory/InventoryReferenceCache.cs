using System.Globalization;
using System.Text.Json;

namespace ModpackTools.Core.Inventory;

public sealed record ReferenceCacheWriteLimits(long MaximumBytes = 64 * 1024 * 1024);
public sealed record ReferenceCacheWriteResult(string Path, int Count);

public static class InventoryReferenceCache
{
    public static ReferenceCacheWriteResult Write(string configurationDirectory, NumberedInventorySelection inventory,
        DateTimeOffset createdUtc, ReferenceCacheWriteLimits? limits = null)
    {
        var maximumBytes = (limits ?? new()).MaximumBytes;
        if (maximumBytes <= 0)
            throw new DomainException("Inventory.InvalidReferenceCacheWritePolicy", "Reference-cache byte quota must be positive",
                "use a positive reference-cache byte quota", target: nameof(ReferenceCacheWriteLimits.MaximumBytes));
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            throw new ArgumentException("A configuration directory is required.", nameof(configurationDirectory));
        var directory = Path.GetFullPath(configurationDirectory);
        var path = Path.Combine(directory, "last-inventory.json");
        var timestamp = createdUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        foreach (var reference in inventory.References) { _ = Kind(reference.Kind); _ = Source(reference.Source); }
        var temporary = Path.Combine(directory, ".last-inventory.json." + Guid.NewGuid().ToString("N") + ".tmp");
        var ownTemporary = false;
        Exception? failure = null;
        try
        {
            Directory.CreateDirectory(directory);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownTemporary = true;
                using var bounded = new CacheOutput(file, maximumBytes, path);
                using var writer = new Utf8JsonWriter(bounded);
                void Text(string field, string value)
                {
                    writer.WriteString(field, value);
                    // Bound pending JSON while batching small fields/rows into file writes.
                    if (writer.BytesPending >= 16 * 1024) writer.Flush();
                }
                writer.WriteStartObject();
                writer.WriteNumber("SchemaVersion", 1);
                Text("CreatedUtc", timestamp);
                Text("ProjectId", inventory.Selection.Project.Id);
                writer.WriteStartArray("Results");
                foreach (var reference in inventory.References)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("Index", reference.Index);
                    Text("Id", reference.Id);
                    Text("Kind", Kind(reference.Kind));
                    Text("Name", reference.Name);
                    Text("Filename", reference.Filename);
                    Text("Source", Source(reference.Source));
                    Text("Selector", reference.Selector);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
            }
            File.Move(temporary, path, overwrite: true);
            ownTemporary = false;
            return new(path, inventory.References.Length);
        }
        catch (Exception exception) when (exception is DomainException or IOException or UnauthorizedAccessException)
        {
            failure = exception;
        }
        finally
        {
            if (ownTemporary)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Keep the primary cause; report cleanup failure with it below.
                    if (failure is not null) failure.Data["ReferenceCache.CleanupFailure"] = exception.Message;
                    else failure = exception;
                }
            }
        }
        var cleanup = failure?.Data["ReferenceCache.CleanupFailure"] as string;
        if (failure is DomainException domain)
        {
            if (cleanup is null) throw domain;
            throw new DomainException(domain.Id["ModpackTools.".Length..], domain.Message, domain.Hint,
                domain.Category, targetData: domain.Target, details: "Temporary cleanup failed: " + cleanup);
        }
        throw new DomainException("Inventory.ReferenceCacheWriteFailed", $"Inventory reference cache '{path}' could not be written",
            "check configuration-directory permissions and retry modpack content list", "WriteError", path,
            details: failure?.Message + (cleanup is null ? "" : "; temporary cleanup failed: " + cleanup));
    }

    private static string Kind(ContentKind kind) => kind switch
    {
        ContentKind.Mod => "mod",
        ContentKind.ResourcePack => "resourcepack",
        ContentKind.ShaderPack => "shaderpack",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static string Source(InventorySource source) => source switch
    {
        InventorySource.Packwiz => "packwiz",
        InventorySource.Local => "local",
        InventorySource.Builtin => "builtin",
        InventorySource.Missing => "missing",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private sealed class CacheOutput(Stream destination, long maximumBytes, string path) : Stream
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
            if (buffer.Length > maximumBytes - written)
                throw new DomainException("Inventory.ReferenceCacheWriteLimitExceeded", "Inventory references exceed the serialized cache byte quota",
                    "run modpack content list with narrower filters", "LimitsExceeded", path);
            destination.Write(buffer);
            written += buffer.Length;
        }
    }
}
