using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core.Build;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Diagnostics;

public static partial class Doctor
{
    private static DoctorCheck Minecraft(CancellationToken cancellation)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(roaming)) return new("OPTIONAL", "warn", "Minecraft Java", "Not detected", "The standard application-data directory is unavailable.", []);
        var minecraft = Path.Combine(roaming, ".minecraft"); var directory = Path.Combine(minecraft, "versions"); var count = 0; var entries = 0;
        try
        {
            WindowsProjectIdentity.GuardAncestors(directory);
            if (Directory.Exists(directory))
                foreach (var version in new DirectoryInfo(directory).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, IgnoreInaccessible = false }))
                {
                    cancellation.ThrowIfCancellationRequested(); if (++entries > 100000) throw new FormatException("Minecraft version discovery exceeds its directory quota.");
                    var path = Path.Combine(version.FullName, version.Name + ".json"); WindowsProjectIdentity.GuardAncestors(path);
                    if (File.Exists(path)) count++;
                }
            if (count != 0) return new("OPTIONAL", "pass", "Minecraft Java", count == 1 ? "1 installed version" : $"{count} installed versions", minecraft, []);
            if (Directory.Exists(minecraft)) return new("OPTIONAL", "warn", "Minecraft Java", "No installed version detected", $"Launcher data exists at '{minecraft}'. Launch Java Edition once to install a version.", []);
            return new("OPTIONAL", "warn", "Minecraft Java", "No standard installation detected", "Custom launcher installations may not be discovered automatically.", []);
        }
        catch (Exception error) when (Recoverable(error)) { return new("OPTIONAL", "warn", "Minecraft Java", "Could not inspect standard installation", error.Message, []); }
    }
    private static async Task<DoctorCheck> Artifact(ProjectContext project, string configuration, string state, PackwizObservation? packwiz,
        string nativeHost, DoctorLimits limits, CancellationToken cancellation)
    {
        var status = await ArtifactFreshness.ReadAsync(project, configuration, state, () => packwiz ?? new(false, null, "missing", null), nativeHost, limits.Freshness, cancellation).ConfigureAwait(false);
        if (status.State == "missing") return new("BUILD ARTIFACT", "info", "Latest .mrpack", "Not built", $"Run: modpack -p {project.Id} build", []);
        if (status.State == "unknown") return new("BUILD ARTIFACT", "warn", "Latest .mrpack", "Could not verify", status.Error, []);
        if (status.State == "current") return new("BUILD ARTIFACT", "pass", "Latest .mrpack", "Current", status.Path, []);
        var items = ImmutableArray.CreateBuilder<DoctorItem>();
        foreach (var item in status.Comparison.Added.Take(4)) items.Add(new("info", "Project adds: " + item.Path));
        foreach (var item in status.Comparison.Changed.Take(4)) items.Add(new("info", "Project changes: " + item.Path));
        foreach (var item in status.Comparison.Removed.Take(4)) items.Add(new("info", "Artifact still contains: " + item.Path));
        if (status.Comparison.Total > items.Count) items.Add(new("info", $"{status.Comparison.Total - items.Count} additional difference(s)."));
        items.Add(new("warn", $"Do not install this artifact. Run: modpack -p {project.Id} build"));
        var built = (status.BuiltUtc ?? throw new DomainException("Doctor.InvalidArtifactStatus", "A verified artifact has no build time", "retry artifact verification")).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return new("BUILD ARTIFACT", "warn", "Latest .mrpack", "Stale", $"{Path.GetFileName(status.Path)}; built {built}; {status.Comparison.Total} difference(s).", items.ToImmutable());
    }
}
