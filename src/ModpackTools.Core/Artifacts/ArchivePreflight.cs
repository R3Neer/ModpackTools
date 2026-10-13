using System.Buffers.Binary;

namespace ModpackTools.Core.Artifacts;

internal static class ArchivePreflight
{
    public static void Validate(FileStream file, ArchiveReadLimits limits)
    {
        if (file.Length < 22) throw new InvalidDataException("The archive has no end record.");
        var tail = new byte[(int)Math.Min(file.Length, 65557)];
        file.Position = file.Length - tail.Length;
        file.ReadExactly(tail);
        var index = tail.Length - 22;
        while (index >= 0 && U32(tail.AsSpan(index)) != 0x06054b50) index--;
        if (index < 0) throw new InvalidDataException("The archive end record was not found.");
        var end = tail.AsSpan(index);
        var footer = file.Length - tail.Length + index;
        if (U16(end[4..]) != U16(end[6..]) || U16(end[8..]) != U16(end[10..]))
            throw new InvalidDataException("Split archives are not supported.");
        ulong entries = U16(end[10..]);
        ulong start = U32(end[16..]);
        // Match the BCL's last EOCD candidate and conditional ZIP64 locator.
        // Never validate one footer and then let ZipArchive consume another.
        if (U16(end[4..]) == ushort.MaxValue || entries == ushort.MaxValue || start == uint.MaxValue)
        {
            if (footer < 20) throw new InvalidDataException("The ZIP64 locator is unavailable.");
            Span<byte> locator = stackalloc byte[20];
            file.Position = footer - 20;
            file.ReadExactly(locator);
            if (U32(locator) == 0x07064b50)
            {
                var position = U64(locator[8..]);
                if (file.Length < 56 || position > (ulong)(file.Length - 56)) throw new InvalidDataException("The ZIP64 end offset is invalid.");
                Span<byte> record = stackalloc byte[56];
                file.Position = (long)position;
                file.ReadExactly(record);
                if (U32(record) != 0x06064b50 || U64(record[24..]) != U64(record[32..]))
                    throw new InvalidDataException("The ZIP64 end record is invalid or split.");
                entries = U64(record[32..]);
                start = U64(record[48..]);
            }
        }
        if (entries > (ulong)limits.MaximumEntries) throw new ArchiveLimitException("The archive entry count exceeds the reader budget.");
        if (start > (ulong)footer) throw new InvalidDataException("The central directory offset is invalid.");
        file.Position = (long)start;
        long bytes = 0;
        ulong actualEntries = 0;
        Span<byte> header = stackalloc byte[46];
        while (file.Position <= footer - 4)
        {
            file.ReadExactly(header[..4]);
            if (U32(header) != 0x02014b50) break;
            if (footer - file.Position < 42) throw new InvalidDataException("A central directory header is truncated.");
            file.ReadExactly(header[4..]);
            var variableBytes = U16(header[28..]) + U16(header[30..]) + U16(header[32..]);
            var length = 46L + variableBytes;
            if (length > limits.MaximumCentralDirectoryBytes - bytes)
                throw new ArchiveLimitException("The actual central directory bytes exceed the reader budget.");
            if (++actualEntries > (ulong)limits.MaximumEntries)
                throw new ArchiveLimitException("The actual archive entry count exceeds the reader budget.");
            if (variableBytes > footer - file.Position) throw new InvalidDataException("A central directory field is truncated.");
            bytes += length;
            file.Position += variableBytes;
        }
        if (actualEntries != entries) throw new InvalidDataException("The archive entry count does not match its end record.");
        file.Position = 0;
    }

    private static ushort U16(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    private static uint U32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    private static ulong U64(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt64LittleEndian(bytes);
}
