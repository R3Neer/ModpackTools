namespace ModpackTools.Core.Inventory;

internal sealed class ReadBudget
{
    private readonly ManagedReadLimits limits;
    private long bytes;
    private long files;
    private long directories;

    public ReadBudget(ManagedReadLimits limits)
    {
        if (limits.MaximumBytes < 1 || limits.MaximumFiles < 1 || limits.MaximumDirectories < 1)
            throw new ArgumentOutOfRangeException(nameof(limits), "Reader budgets must be positive.");
        this.limits = limits;
    }

    public void File(string path)
    {
        if (++files > limits.MaximumFiles) Exceeded(path, "matched-file count");
    }

    public void Directory(string path)
    {
        if (++directories > limits.MaximumDirectories) Exceeded(path, "directory count");
    }

    public void Text(string path, long count)
    {
        if (count > limits.MaximumBytes - bytes) Exceeded(path, "metadata byte count");
        bytes += count;
    }

    private static void Exceeded(string path, string budget) => throw new DomainException(
        "Content.ReadLimitExceeded", $"The managed-content {budget} exceeds the reader budget",
        "reduce the metadata size or split the project before retrying", "LimitsExceeded", path);
}
