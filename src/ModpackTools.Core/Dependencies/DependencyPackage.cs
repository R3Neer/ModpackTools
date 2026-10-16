using System.Collections.Immutable;

namespace ModpackTools.Core.Dependencies;

// Trust comes from the caller's pinned release metadata, never from the archive.
public sealed record DependencyPackage(string Version, string ArchiveSha256, string ExecutableSha256,
    string Executable = "packwiz.exe", string License = "LICENSE.packwiz.txt")
{
    public static DependencyPackage Packwiz { get; } = new("dfd8b68",
        "899CA7D4DF273BD4F2B916FB7361E5B32B55F092393C2C90FA47B14C38509F31",
        "7182F0B513A4FF117CA252B7EAEA2CEDEEDE19DD9A0BED77A10403D75A3C9295");
    public static Uri PackwizUri { get; } = new("https://github.com/R3Neer/ModpackTools/releases/download/v1.0.0/ModpackTools-Packwiz-win-x64-dfd8b68.zip");
}
public sealed record DependencyArchiveLimits(long MaximumArchiveBytes = 256 * 1024 * 1024,
    long MaximumExpandedBytes = 512 * 1024 * 1024, long MaximumLicenseBytes = 1024 * 1024, int MaximumEntries = 256);
public sealed record PreparedDependencyFile(string Name, string Sha256, long Length);
public sealed record PreparedDependency(string Directory, string Version, ImmutableArray<PreparedDependencyFile> Files);
