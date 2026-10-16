using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ModpackTools.Core.Source;

public static class TextHash
{
    public static string Compute(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, text);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    internal static void Append(IncrementalHash hash, string text)
    {
        var encoder = Encoding.UTF8.GetEncoder();
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            var remaining = text.AsSpan();
            bool complete;
            do
            {
                encoder.Convert(remaining, buffer.AsSpan(0, 4096), flush: true, out var consumed, out var written, out complete);
                hash.AppendData(buffer.AsSpan(0, written));
                remaining = remaining[consumed..];
            } while (!complete);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}

public static class SourceFingerprint
{
    public static string Health(SourceTreeSnapshot snapshot, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        culture ??= CultureInfo.CurrentCulture;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var first = true;
        foreach (var (path, digest) in snapshot.Files.Where(pair => !culture.CompareInfo.IsPrefix(pair.Key, "dist/", CompareOptions.IgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Create(culture, true)).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!first) TextHash.Append(hash, "\n");
            first = false;
            TextHash.Append(hash, path);
            TextHash.Append(hash, "=");
            TextHash.Append(hash, digest);
        }
        TextHash.Append(hash, "|validator=3");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string All(SourceTreeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("ModpackTools/source-snapshot/v1\0"u8);
        Span<byte> length = stackalloc byte[4];
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            foreach (var (path, digest) in snapshot.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                BinaryPrimitives.WriteInt32BigEndian(length, path.Length);
                hash.AppendData(length);
                for (var offset = 0; offset < path.Length;)
                {
                    var count = Math.Min(path.Length - offset, 2048);
                    for (var index = 0; index < count; index++)
                        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(index * 2, 2), path[offset + index]);
                    hash.AppendData(buffer.AsSpan(0, count * 2));
                    offset += count;
                }
                hash.AppendData(Convert.FromHexString(digest));
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
