using System.Text;
using System.Text.Json;

namespace ModpackTools.Core.Data;

internal sealed record OwnedCacheData(JsonElement Data, CacheDateTable Dates);
internal readonly record struct CacheDecodeLimits(long MaximumBytes, int MaximumDepth,
    int MaximumNormalizedCharacters, int MaximumTokenCharacters, int MaximumNodes);

internal static class LegacyCacheFile
{
    public static OwnedCacheData? Read(string path, CacheDecodeLimits limits,
        Func<string, string?, DomainException> invalid, Func<string, string, DomainException> limit)
    {
        try
        {
            FileStream file;
            try { file = File.OpenRead(path); }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return null; }
            catch (UnauthorizedAccessException) when (Directory.Exists(path)) { return null; }
            using (file)
            using (var bounded = new CacheInput(file, limits.MaximumBytes))
            using (var reader = new StreamReader(bounded, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true))
            {
                var decoded = CacheTokenCodec.Read(reader, limits.MaximumDepth, limits.MaximumNormalizedCharacters,
                    limits.MaximumTokenCharacters, limits.MaximumNodes);
                if (decoded is null) return null;
                using var document = decoded.Document;
                JsonDisplayProjection.Validate(document.RootElement);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0) return null;
                    if (root.GetArrayLength() == 1) root = root[0];
                }
                return root.ValueKind == JsonValueKind.Null ? null : new(root.Clone(), decoded.Dates);
            }
        }
        catch (JsonException exception) when (exception.Message.StartsWith("The maximum configured depth of ", StringComparison.Ordinal))
        { throw limit(path, exception.Message); }
        catch (Newtonsoft.Json.JsonReaderException exception) when (exception.Message.StartsWith("The reader's MaxDepth of ", StringComparison.Ordinal))
        { throw limit(path, exception.Message); }
        catch (CacheJsonLimitException exception) { throw limit(path, exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or Newtonsoft.Json.JsonException or FormatException or OverflowException or InvalidOperationException or InvalidCastException or ArgumentException)
        { throw invalid(path, null); }
    }

    private sealed class CacheInput(Stream source, long maximumBytes) : Stream
    {
        private long read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = source.Read(buffer);
            if (count > maximumBytes - read) throw new CacheJsonLimitException("Actual file bytes exceed the reader quota.");
            read += count;
            return count;
        }
    }
}
