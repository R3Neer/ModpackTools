using System.Buffers;
using System.IO.Compression;
using System.Text;

namespace ModpackTools.Core.Artifacts;

public enum ArchiveReadStatus { Read, Unreadable, Limited }
public sealed record RootArchiveDeclarations(RootManifestDeclarations Root, ArchiveReadStatus Status);
public sealed record RootArchiveLimits
{
    public ArchiveReadLimits Archive { get; init; } = new();
    public ForgeManifestLimits Declarations { get; init; } = new();
    public long MaximumAggregateMetadataBytes { get; init; } = 24 * 1024 * 1024;
}

internal enum LoaderManifestEntry { Fabric, Quilt, Forge, NeoForge, Manifest, JarJar }
internal sealed class RootArchiveEntryException(ManifestDiagnostic diagnostic) : Exception(diagnostic.Message)
{ public ManifestDiagnostic Diagnostic { get; } = diagnostic; }

public static class RootArchiveReader
{
    public static RootArchiveDeclarations Read(string path, string loader, RootArchiveLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(path); ArgumentNullException.ThrowIfNull(loader);
        limits ??= new();
        Validate(limits);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ArchivePreflight.Validate(file, limits.Archive);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            return ReadOpened(archive, loader, limits);
        }
        catch (RootArchiveEntryException exception)
        { return Failed(exception.Diagnostic, exception.Diagnostic.Code == "ArchiveLimit" ? ArchiveReadStatus.Limited : ArchiveReadStatus.Read); }
        catch (ArchiveLimitException exception)
        { return Failed(new("ArchiveLimit", "JAR could not be read: archive census exceeds its resource limit", "", exception.Message, "inspect the central directory or use explicit archive quotas"), ArchiveReadStatus.Limited); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Failed(new("ArchiveUnreadable", "JAR could not be read: " + path, "", exception.Message, "check archive integrity and readonly access"), ArchiveReadStatus.Unreadable); }
    }
    internal static RootArchiveDeclarations ReadOpened(ZipArchive archive, string loader, RootArchiveLimits limits, Action<int>? chargeMetadata = null, DeclarationAllowance? allowance = null)
    {
        try
        {
            var reader = new EntryReader(archive, limits, chargeMetadata);
            var fabric = reader.Read(LoaderManifestEntry.Fabric);
            RootManifestDeclarations result;
            if (string.Equals(loader, "forge", StringComparison.InvariantCultureIgnoreCase)
                || string.Equals(loader, "neoforge", StringComparison.InvariantCultureIgnoreCase))
                result = ForgeRootManifestParser.ParseEntries(reader.Read, loader, limits.Declarations, allowance);
            else
            {
                var quilt = string.Equals(loader, "quilt", StringComparison.InvariantCultureIgnoreCase) ? reader.Read(LoaderManifestEntry.Quilt) : null;
                result = RootManifestParser.ParseOwned(fabric, quilt, loader, limits.Declarations.Declarations, allowance);
            }
            return new(result, result.Diagnostics.Any(item => item.Code is "ArchiveLimit" or "DeclarationLimit") ? ArchiveReadStatus.Limited : ArchiveReadStatus.Read);
        }
        catch (RootArchiveEntryException exception)
        { return Failed(exception.Diagnostic, exception.Diagnostic.Code == "ArchiveLimit" ? ArchiveReadStatus.Limited : ArchiveReadStatus.Read); }
    }
    private static RootArchiveDeclarations Failed(ManifestDiagnostic diagnostic, ArchiveReadStatus status) =>
        new(new([], [diagnostic], [], RootManifestStatus.Incomplete, false), status);
    internal static void Validate(RootArchiveLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits.Archive); ArgumentNullException.ThrowIfNull(limits.Declarations);
        ForgeRootManifestParser.ValidatePolicy(limits.Declarations);
        var invalid = limits.Archive.MaximumMetadataBytes <= 0 ? nameof(limits.Archive.MaximumMetadataBytes)
            : limits.Archive.MaximumCentralDirectoryBytes <= 0 ? nameof(limits.Archive.MaximumCentralDirectoryBytes)
            : limits.Archive.MaximumEntries <= 0 ? nameof(limits.Archive.MaximumEntries)
            : limits.MaximumAggregateMetadataBytes <= 0 ? nameof(limits.MaximumAggregateMetadataBytes) : null;
        if (invalid is not null) throw new DomainException("Metadata.InvalidArchivePolicy", "Root archive limits are invalid",
            "use positive metadata, central-directory, entry-count and aggregate quotas", target: invalid);
    }
    private static string Name(LoaderManifestEntry entry) => entry switch
    {
        LoaderManifestEntry.Fabric => "fabric.mod.json",
        LoaderManifestEntry.Quilt => "quilt.mod.json",
        LoaderManifestEntry.Forge => "META-INF/mods.toml",
        LoaderManifestEntry.NeoForge => "META-INF/neoforge.mods.toml",
        LoaderManifestEntry.Manifest => "META-INF/MANIFEST.MF",
        LoaderManifestEntry.JarJar => "META-INF/jarjar/metadata.json",
        _ => throw new ArgumentOutOfRangeException(nameof(entry))
    };
    private sealed class EntryReader(ZipArchive archive, RootArchiveLimits limits, Action<int>? chargeMetadata)
    {
        private long aggregate;
        public string? Read(LoaderManifestEntry requested)
        {
            var name = Name(requested);
            try
            {
                var entry = archive.GetEntry(name);
                if (entry is null) return null;
                if (entry.Length > limits.Archive.MaximumMetadataBytes) Limit(nameof(limits.Archive.MaximumMetadataBytes), name);
                using var stream = new CountedReadStream(entry.Open(), limits.Archive.MaximumMetadataBytes, count =>
                {
                    if (count > limits.MaximumAggregateMetadataBytes - aggregate) Limit(nameof(limits.MaximumAggregateMetadataBytes), name);
                    chargeMetadata?.Invoke(count); aggregate += count;
                }, name);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var buffer = ArrayPool<char>.Shared.Rent(4096);
                try
                {
                    var text = new StringBuilder();
                    int count;
                    while ((count = reader.Read(buffer, 0, 4096)) != 0)
                    {
                        if (count > limits.Declarations.Declarations.MaximumTextCharacters - text.Length)
                            Limit(nameof(limits.Declarations.Declarations.MaximumTextCharacters), name);
                        text.Append(buffer, 0, count);
                    }
                    return text.ToString();
                }
                finally { ArrayPool<char>.Shared.Return(buffer); }
            }
            catch (RootArchiveEntryException) { throw; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            { throw new RootArchiveEntryException(new("MetadataReadFailed", "Manifest could not be fully read: read metadata '" + name + "'", name, exception.Message, "check the selected archive entry and compression")); }
        }
        private static void Limit(string property, string name) => throw new RootArchiveEntryException(new("ArchiveLimit",
            "Manifest could not be fully read: metadata resource limit exceeded", name, property + " exceeded.", "inspect the entry or use explicit metadata quotas"));
    }
    private sealed class CountedReadStream(Stream source, long maximum, Action<int> aggregate, string name) : Stream
    {
        private long read;
        private int Count(int count)
        {
            if (count > maximum - read) throw new RootArchiveEntryException(new("ArchiveLimit", "Manifest could not be fully read: metadata resource limit exceeded",
                name, "MaximumMetadataBytes exceeded by actual decompressed bytes.", "inspect the entry or use explicit metadata quotas"));
            aggregate(count); read += count; return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => Count(source.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Count(source.Read(buffer));
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
