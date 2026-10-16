namespace ModpackTools.Core.Artifacts;

public sealed record ArchiveReadLimits(long MaximumMetadataBytes = 4 * 1024 * 1024,
    long MaximumCentralDirectoryBytes = 64 * 1024 * 1024, int MaximumEntries = 100000)
{
    internal void Validate()
    {
        if (MaximumMetadataBytes < 1 || MaximumCentralDirectoryBytes < 1 || MaximumEntries < 1)
            throw new ArgumentOutOfRangeException("limits", "Archive budgets must be positive.");
    }
}

internal sealed class ArchiveLimitException(string message) : Exception(message);
