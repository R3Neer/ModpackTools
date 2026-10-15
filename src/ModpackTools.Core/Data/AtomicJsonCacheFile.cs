using System.Text.Json;

namespace ModpackTools.Core.Data;

internal static class AtomicJsonCacheFile
{
    public static void Write(string directory, string path, long maximumBytes, Action<Utf8JsonWriter> serialize,
        Func<DomainException> limit, Func<string?, DomainException> failed,
        Action? beforeCommit = null, JsonWriterOptions writerOptions = default)
    {
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var ownTemporary = false;
        Exception? failure = null;
        try
        {
            Directory.CreateDirectory(directory);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownTemporary = true;
                using var bounded = new CacheOutput(file, maximumBytes, limit);
                using var writer = new Utf8JsonWriter(bounded, writerOptions);
                serialize(writer);
                writer.Flush();
            }
            beforeCommit?.Invoke();
            File.Move(temporary, path, overwrite: true);
            ownTemporary = false;
            return;
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
        throw failed(failure?.Message + (cleanup is null ? "" : "; temporary cleanup failed: " + cleanup));
    }

    private sealed class CacheOutput(Stream destination, long maximumBytes, Func<DomainException> limit) : Stream
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
            if (buffer.Length > maximumBytes - written) throw limit();
            destination.Write(buffer);
            written += buffer.Length;
        }
    }
}
