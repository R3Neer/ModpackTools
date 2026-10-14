using System.Buffers;
using System.Collections.Immutable;
using System.IO.Compression;

namespace ModpackTools.Core.Artifacts;

public sealed record ArtifactMod(DeclaredMod Declaration, bool Nested, ImmutableArray<string> EntryChain);
public sealed record ArtifactDiagnostic(ManifestDiagnostic Diagnostic, ImmutableArray<string> EntryChain);
public enum ArtifactTraversalStatus { Complete, Incomplete, Limited, Unreadable }
public sealed record ArtifactArchiveDeclarations(RootArchiveDeclarations? Root,
    ImmutableArray<ArtifactMod> Mods, ImmutableArray<ArtifactDiagnostic> Diagnostics, ArtifactTraversalStatus Status);
public sealed record ArtifactTraversalLimits
{
    public RootArchiveLimits Root { get; init; } = new();
    public int MaximumDepth { get; init; } = 16;
    public long MaximumChildBytes { get; init; } = 128 * 1024 * 1024;
    public long MaximumCopiedBytes { get; init; } = 512 * 1024 * 1024;
    public int MaximumNestedAttempts { get; init; } = 1000;
    public long MaximumMetadataBytes { get; init; } = 24 * 1024 * 1024;
    public long MaximumCentralBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 100000;
    public int MaximumMods { get; init; } = 100000;
    public int MaximumDiagnostics { get; init; } = 100000;
}

public static class ArtifactArchiveReader
{
    public static ArtifactArchiveDeclarations Read(string path, string loader, ArtifactTraversalLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(path); ArgumentNullException.ThrowIfNull(loader);
        limits ??= new(); RootArchiveReader.Validate(limits.Root);
        var invalid = limits.MaximumDepth is < 0 or > 16 ? nameof(limits.MaximumDepth)
            : limits.MaximumChildBytes <= 0 ? nameof(limits.MaximumChildBytes)
            : limits.MaximumCopiedBytes <= 0 ? nameof(limits.MaximumCopiedBytes)
            : limits.MaximumNestedAttempts <= 0 ? nameof(limits.MaximumNestedAttempts)
            : limits.MaximumMetadataBytes <= 0 ? nameof(limits.MaximumMetadataBytes)
            : limits.MaximumCentralBytes <= 0 ? nameof(limits.MaximumCentralBytes)
            : limits.MaximumEntries <= 0 ? nameof(limits.MaximumEntries)
            : limits.MaximumMods <= 0 ? nameof(limits.MaximumMods)
            : limits.MaximumDiagnostics <= 0 ? nameof(limits.MaximumDiagnostics) : null;
        if (invalid is not null) throw new DomainException("Metadata.InvalidTraversalPolicy", "Archive traversal limits are invalid",
            "use positive quotas and depth between 0 and 16", target: invalid);
        var scope = new Scope(limits, loader);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            scope.Preflight(file);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            scope.Visit(archive, [], 0);
        }
        catch (TraversalLimitException exception) { scope.Terminate(exception, []); }
        catch (ArchiveLimitException exception)
        {
            scope.Status = ArtifactTraversalStatus.Limited;
            scope.Warn(new("ArchiveLimit", "JAR could not be read: archive census exceeds its resource limit", "",
                exception.Message, "inspect the central directory or use explicit archive quotas"), []);
        }
        catch (Exception exception) when (ReadFailure(exception))
        {
            scope.Status = ArtifactTraversalStatus.Unreadable;
            scope.Warn(new("ArchiveUnreadable", "JAR could not be read: " + path, "", exception.Message,
                "check archive integrity and readonly access"), []);
        }
        return new(scope.Root, [.. scope.Mods], [.. scope.Diagnostics], scope.Status);
    }

    private static bool ReadFailure(Exception exception) => exception is IOException or InvalidDataException
        or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException;
    private sealed class TraversalLimitException(string message, ImmutableArray<string> chain = default) : Exception(message)
    { public ImmutableArray<string> Chain { get; } = chain; }

    private sealed class Scope(ArtifactTraversalLimits limits, string loader)
    {
        public RootArchiveDeclarations? Root { get; private set; }
        public List<ArtifactMod> Mods { get; } = [];
        public List<ArtifactDiagnostic> Diagnostics { get; } = [];
        public ArtifactTraversalStatus Status { get; set; } = ArtifactTraversalStatus.Complete;
        private long copied, metadata, central;
        private int attempts, entries;
        private bool exhausted;

        public void Preflight(FileStream file)
        {
            // The scan allocates no entry index. Debit shared census before opening
            // ZipArchive; a local scan failure and a global exhaustion differ.
            var census = ArchivePreflight.Validate(file, limits.Root.Archive);
            if (census.Bytes > limits.MaximumCentralBytes - central) Limit(nameof(limits.MaximumCentralBytes));
            if (census.Entries > limits.MaximumEntries - entries) Limit(nameof(limits.MaximumEntries));
            central += census.Bytes; entries += census.Entries;
        }

        public void Visit(ZipArchive archive, ImmutableArray<string> chain, int depth)
        {
            if (exhausted) return;
            try
            {
                if (depth > limits.MaximumDepth)
                {
                    Incomplete();
                    Warn(new("NestedDepth", "Nested JAR depth exceeds " + limits.MaximumDepth, "", null,
                        "inspect nested archive declarations"), chain); return;
                }
                var root = RootArchiveReader.ReadOpened(archive, loader, limits.Root, count =>
                {
                    if (count > limits.MaximumMetadataBytes - metadata)
                    {
                        exhausted = true;
                        throw new RootArchiveEntryException(new("TraversalLimit", "Manifest could not be fully read: traversal resource limit exceeded",
                            "", nameof(limits.MaximumMetadataBytes), "inspect nested metadata or use explicit traversal quotas"));
                    }
                    metadata += count;
                }, new(limits.MaximumMods - Mods.Count, limits.MaximumDiagnostics - Diagnostics.Count, depth != 0));
                if (depth == 0) Root = root;
                foreach (var mod in root.Root.Mods)
                {
                    if (Mods.Count == limits.MaximumMods) Limit(nameof(limits.MaximumMods));
                    Mods.Add(new(mod, depth != 0, chain));
                }
                foreach (var diagnostic in root.Root.Diagnostics)
                {
                    if (diagnostic.Code == "TraversalLimit") { Terminate(new(diagnostic.Details ?? "Declaration allowance exceeded."), chain); return; }
                    if (depth == 0 || diagnostic.Code != "NoSupportedManifest") Warn(diagnostic, chain);
                }
                if (root.Root.Status == RootManifestStatus.Incomplete) Incomplete();
                if (root.Status == ArchiveReadStatus.Limited)
                {
                    Status = ArtifactTraversalStatus.Limited; return;
                }
                if (exhausted) { Status = ArtifactTraversalStatus.Limited; return; }
                if (!root.Root.CanTraverseNested) return;
                foreach (var name in root.Root.NestedPaths)
                {
                    if (exhausted) return;
                    var childChain = chain.Add(name);
                    var entry = archive.GetEntry(name);
                    if (entry is null || entry.Length > limits.MaximumChildBytes)
                    {
                        Incomplete();
                        Warn(new("NestedUnavailable", "Nested JAR '" + name + "' is unavailable or too large", name,
                            null, "check the declared entry and child size"), childChain); continue;
                    }
                    try
                    {
                        if (attempts == limits.MaximumNestedAttempts) Limit(nameof(limits.MaximumNestedAttempts));
                        attempts++;
                        using var source = entry.Open();
                        using var spool = new FileStream(Path.Combine(Path.GetTempPath(), "modpacktools-nested-" + Guid.NewGuid().ToString("N") + ".tmp"),
                            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose);
                        Copy(source, spool); spool.Position = 0;
                        Preflight(spool);
                        using var child = new ZipArchive(spool, ZipArchiveMode.Read, leaveOpen: true);
                        Visit(child, childChain, depth + 1);
                    }
                    catch (TraversalLimitException exception) { Terminate(exception, childChain); return; }
                    catch (ArchiveLimitException exception)
                    {
                        Status = ArtifactTraversalStatus.Limited;
                        Warn(new("ArchiveLimit", "Manifest could not be fully read: nested JAR '" + name + "'", name,
                            exception.Message, "inspect the child central directory or use explicit archive quotas"), childChain); return;
                    }
                    catch (Exception exception) when (ReadFailure(exception))
                    {
                        Incomplete();
                        Warn(new("NestedReadFailed", "Manifest could not be fully read: nested JAR '" + name + "'", name,
                            exception.Message, "check the selected child archive and temporary storage"), childChain); return;
                    }
                }
            }
            catch (TraversalLimitException exception) { Terminate(exception, chain); }
        }

        private void Copy(Stream source, FileStream destination)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                long childBytes = 0;
                int count;
                while ((count = source.Read(buffer, 0, 65536)) != 0)
                {
                    if (count > limits.MaximumChildBytes - childBytes) Limit(nameof(limits.MaximumChildBytes));
                    if (count > limits.MaximumCopiedBytes - copied) Limit(nameof(limits.MaximumCopiedBytes));
                    childBytes += count; copied += count; destination.Write(buffer, 0, count);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public void Warn(ManifestDiagnostic diagnostic, ImmutableArray<string> chain)
        {
            if (Diagnostics.Count == limits.MaximumDiagnostics) throw new TraversalLimitException(nameof(limits.MaximumDiagnostics) + " exceeded.", chain);
            Diagnostics.Add(new(diagnostic, chain));
        }
        private void Incomplete() { if (Status == ArtifactTraversalStatus.Complete) Status = ArtifactTraversalStatus.Incomplete; }
        public void Terminate(TraversalLimitException exception, ImmutableArray<string> chain)
        {
            if (!exception.Chain.IsDefault) chain = exception.Chain;
            exhausted = true; Status = ArtifactTraversalStatus.Limited;
            Diagnostics.Add(new(new("TraversalLimit", "Manifest could not be fully read: traversal resource limit exceeded",
                chain.IsEmpty ? "" : chain[^1], exception.Message, "inspect the archive or use explicit traversal quotas"), chain));
        }
        private static void Limit(string property) => throw new TraversalLimitException(property + " exceeded.");
    }
}
