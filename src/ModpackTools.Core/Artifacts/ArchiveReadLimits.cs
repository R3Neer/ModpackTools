namespace ModpackTools.Core.Artifacts;

public sealed record ArchiveReadLimits(long MaximumMetadataBytes = 4 * 1024 * 1024,
    long MaximumCentralDirectoryBytes = 64 * 1024 * 1024, int MaximumEntries = 100000);

internal sealed class ArchiveLimitException(string message) : Exception(message);
