namespace ModpackTools.Core.Inventory;

public enum ContentKind { Mod, ResourcePack, ShaderPack }

public sealed record ManagedContentItem(string Id, ContentKind Kind, string Name,
    string? Filename, string Side, string? VersionId, string MetadataPath, bool Pinned)
{
    public ManagedInstallFacts? InstallFacts { get; init; }
}

public sealed record ManagedReadLimits(long MaximumBytes = 64 * 1024 * 1024,
    int MaximumFiles = 100000, int MaximumDirectories = 100000);
