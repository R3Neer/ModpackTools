namespace ModpackTools.Core;

public sealed record VersionInfo(string Version);

public static class ProductVersion
{
    public static VersionInfo Current { get; } = new("4.0.2");
}
