namespace ModpackTools.Core.Providers;

public sealed record ProviderModelLimits(long MaximumDecodedBytes = 64L * 1024 * 1024,
    int MaximumVersions = 10000, int MaximumCollectionEntries = 100000);

internal sealed class ProviderModelBudget
{
    private readonly ProviderModelLimits limits;
    private readonly object sync = new();
    private long bytes;
    private int versions, entries;
    public ProviderModelBudget(ProviderModelLimits? limits = null)
    {
        this.limits = limits ?? new();
        var invalid = this.limits.MaximumDecodedBytes <= 0 ? nameof(this.limits.MaximumDecodedBytes)
            : this.limits.MaximumVersions <= 0 ? nameof(this.limits.MaximumVersions)
            : this.limits.MaximumCollectionEntries <= 0 ? nameof(this.limits.MaximumCollectionEntries) : null;
        if (invalid is not null) throw new DomainException("Metadata.InvalidProviderModelPolicy", "Provider model limits are invalid",
            "use positive invocation model quotas", target: invalid);
    }
    public void Bytes(int count)
    {
        lock (sync)
        { if (count > limits.MaximumDecodedBytes - bytes) throw Limit(nameof(limits.MaximumDecodedBytes)); bytes += count; }
    }
    public void Version()
    {
        lock (sync)
        { if (versions >= limits.MaximumVersions) throw Limit(nameof(limits.MaximumVersions)); versions++; }
    }
    public void Entry()
    {
        lock (sync)
        { if (entries >= limits.MaximumCollectionEntries) throw Limit(nameof(limits.MaximumCollectionEntries)); entries++; }
    }
    private static DomainException Limit(string target) => new("Metadata.ProviderModelLimit", "Provider models exceed their invocation resource limit",
        "use explicit bounded model quotas", "LimitsExceeded", target);
}
