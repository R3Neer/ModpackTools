using System.Collections.Immutable;
using ModpackTools.Core.Transactions;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Distribution;

public sealed record NativeInstallationResult(string Action, string Root, string Version, string BuildId, string Generation,
    bool RootCreated, bool VersionPublished, bool BinReady, bool ControlSwitched, bool ActiveReady,
    string Launcher, string NuModule, ImmutableArray<TransactionDiagnostic> Diagnostics, int DiagnosticsOmitted = 0);

public static class NativeInstallation
{
    public static async Task<NativeInstallationResult> ApplyAsync(string archive, string sha256, string root, string? nu,
        string stateDirectory, string nativeHost, bool install, bool allowDowngrade = false,
        CancellationToken cancellation = default, Action<NativePackageProgress>? progress = null)
    {
        root = FileTreeTransaction.CanonicalFutureDirectory(root); stateDirectory = FileTreeTransaction.CanonicalFutureDirectory(stateDirectory);
        if (StringComparer.OrdinalIgnoreCase.Equals(root, stateDirectory) || TransactionFiles.IsBelow(root, stateDirectory) || TransactionFiles.IsBelow(stateDirectory, root))
            throw Error("UnsafeState", "Application and recovery-state directories must be separate trees", stateDirectory);
        var temporary = FileTreeTransaction.CanonicalFutureDirectory(Path.GetTempPath());
        if (StringComparer.OrdinalIgnoreCase.Equals(temporary, root) || TransactionFiles.IsBelow(temporary, root))
            throw Error("UnsafePreparation", "Temporary package preparation must remain outside the application directory", temporary);
        var prepared = NativePackageArchive.Prepare(archive, sha256, temporary, cancellation: cancellation, progress: progress);
        NativeInstallationResult result;
        var created = false; var published = false; var binReady = false;
        try
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(prepared.Identity.CanonicalRoot, root) || TransactionFiles.IsBelow(prepared.Identity.CanonicalRoot, root)
                || TransactionFiles.IsBelow(root, prepared.Identity.CanonicalRoot)) throw Error("UnsafePreparation", "Prepared and installed application trees must be separate", prepared.Directory);
            if (StringComparer.OrdinalIgnoreCase.Equals(prepared.Identity.CanonicalRoot, stateDirectory) || TransactionFiles.IsBelow(prepared.Identity.CanonicalRoot, stateDirectory)
                || TransactionFiles.IsBelow(stateDirectory, prepared.Identity.CanonicalRoot)) throw Error("UnsafeState", "Prepared packages and recovery-state directories must be separate trees", stateDirectory);
            var selectedNu = install ? Path.GetFullPath(nu ?? throw Error("NuRequired", "Select the existing Nushell executable explicitly for a new installation", root))
                : NativeInstallationRoot.Read(root).NuExecutable;
            // Reject malformed roles/unsupported Nu before reserving a new root.
            await NativeInstallationSmoke.VerifyAsync(prepared, selectedNu, nativeHost, cancellation).ConfigureAwait(false);
            if (install) { _ = NativeInstallationRoot.Create(root, selectedNu); created = true; }
            await using var admission = await NativeInstallationRoot.AcquireAsync(root).ConfigureAwait(false);
            NativeRetirement.AssertAvailable(root, prepared.Manifest.BuildId);
            var controlRoot = Path.Combine(root, "control"); var previous = NativeInstallationControl.Read(controlRoot);
            if (previous is not null)
            {
                var comparison = VersionOrdering.Compare(prepared.Manifest.Version, previous.Current.Version);
                if (comparison is null) throw Error("VersionUnordered", "Package and installed public versions cannot be compared", prepared.Manifest.Version);
                if (comparison < 0 && !allowDowngrade) throw Error("DowngradeRefused", "An older public version requires explicit allow-downgrade or rollback", prepared.Manifest.Version);
            }
            var version = await NativeVersionPublication.PublishAsync(prepared, FileTreeTransaction.InspectIdentity(Path.Combine(root, "versions")),
                cancellation: cancellation, progress: progress).ConfigureAwait(false); published = version.Published;
            await NativeInstallationSmoke.VerifyAsync(version.Package, selectedNu, nativeHost, cancellation).ConfigureAwait(false);
            NativeInstallationBootstrap.Ensure(root, version.Package, previous is not null, cancellation); binReady = true;
            admission.AssertCurrent();
            var control = await NativeInstallationControl.SwitchAsync(FileTreeTransaction.InspectIdentity(controlRoot), stateDirectory, version.Package,
                previous?.Generation, cancellation, verifyBootstrap: (path, token) => NativeInstallationSmoke.VerifyBootstrapAsync(path, selectedNu, nativeHost, token)).ConfigureAwait(false);
            result = Receipt(install ? "install" : "update", root, control, created, published);
            if (control.Applied && NativeRetirementCensus.Capture(admission.Owner, control.Current) is { } census)
                result = result with { Diagnostics = result.Diagnostics.Add(census) };
        }
        catch (Exception error)
        {
            created |= error.Data["ModpackTools.Installation.RootCreated"] is true;
            error.Data["ModpackTools.Installation.ApplicationRoot"] = root;
            error.Data["ModpackTools.Installation.PreparedDirectory"] = prepared.Directory;
            error.Data["ModpackTools.Installation.RootCreated"] = created;
            error.Data["ModpackTools.Installation.VersionPublished"] = published;
            error.Data["ModpackTools.Installation.BinReady"] = binReady;
            if (error.Data["ModpackTools.RetainStage"] is not true)
            {
                try { Release(prepared); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
                {
                    var retained = Error("PreparationRetained", "Installation failed; preserve its prepared package for inspection", prepared.Directory, error.Message + "; " + cleanup.Message);
                    retained.Data["ModpackTools.RetainStage"] = true;
                    retained.Data["ModpackTools.Installation.RootCreated"] = created;
                    retained.Data["ModpackTools.Installation.VersionPublished"] = published;
                    retained.Data["ModpackTools.Installation.BinReady"] = binReady;
                    retained.Data["ModpackTools.Installation.ApplicationRoot"] = root;
                    retained.Data["ModpackTools.Installation.PreparedDirectory"] = prepared.Directory; throw retained;
                }
            }
            throw;
        }
        try { Release(prepared); return result; }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
        {
            return result with { Diagnostics = result.Diagnostics.Add(new("Installation.PreparationCleanupPending", prepared.Directory, cleanup.Message)) };
        }
    }

    public static async Task<NativeInstallationResult> RollbackAsync(string root, string stateDirectory, string nativeHost,
        string? buildId = null, CancellationToken cancellation = default)
    {
        root = FileTreeTransaction.CanonicalFutureDirectory(root); stateDirectory = FileTreeTransaction.CanonicalFutureDirectory(stateDirectory);
        if (StringComparer.OrdinalIgnoreCase.Equals(root, stateDirectory) || TransactionFiles.IsBelow(root, stateDirectory) || TransactionFiles.IsBelow(stateDirectory, root))
            throw Error("UnsafeState", "Application and recovery-state directories must be separate trees", stateDirectory);
        await using var admission = await NativeInstallationRoot.AcquireAsync(root).ConfigureAwait(false);
        var controlRoot = Path.Combine(root, "control");
        var previous = NativeInstallationControl.Read(controlRoot) ?? throw Error("NotReady", "Installation has no active version", root);
        buildId ??= previous.PreviousBuildId;
        var selected = previous.Retired.SingleOrDefault(value => value.BuildId == buildId)
            ?? throw Error("RollbackUnavailable", "The requested previous version is not retained", buildId ?? root);
        NativeRetirement.AssertAvailable(root, selected.BuildId);
        var version = NativePackageArchive.OpenInstalled(Path.Combine(root, "versions", selected.BuildId), selected, cancellation);
        await NativeInstallationSmoke.VerifyAsync(version, admission.Owner.NuExecutable, nativeHost, cancellation).ConfigureAwait(false);
        NativeInstallationBootstrap.Ensure(root, version, active: true, cancellation);
        var control = await NativeInstallationControl.SwitchAsync(FileTreeTransaction.InspectIdentity(controlRoot), stateDirectory, version,
            previous.Generation, cancellation, verifyBootstrap: (path, token) => NativeInstallationSmoke.VerifyBootstrapAsync(path, admission.Owner.NuExecutable, nativeHost, token)).ConfigureAwait(false);
        var result = Receipt("rollback", root, control, false, false);
        if (control.Applied && NativeRetirementCensus.Capture(admission.Owner, control.Current) is { } census)
            result = result with { Diagnostics = result.Diagnostics.Add(census) };
        return result;
    }

    private static NativeInstallationResult Receipt(string action, string root, NativeControlSwitch control, bool created, bool published) =>
        new(action, root, control.Current.Current.Version, control.Current.Current.BuildId, control.Current.Generation,
            created, published, true, control.Applied, true, Path.Combine(root, "bin", "modpack.exe"), Path.Combine(root, "control", "Nushell", "modpack.nu"),
            control.Diagnostics.Take(8).ToImmutableArray(), Math.Max(0, control.Diagnostics.Length - 8));

    private static void Release(NativePreparedPackage prepared)
    {
        var files = prepared.Files.ToDictionary(file => file.Path, file => (NativePackageFile?)file, StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in prepared.Files)
            for (var slash = file.Path.IndexOf('/'); slash >= 0; slash = file.Path.IndexOf('/', slash + 1)) directories.Add(file.Path[..slash]);
        NativePackageArchive.Cleanup(prepared.Directory, prepared.Identity, files, directories, new());
    }
    private static DomainException Error(string id, string message, string path, string? details = null) => NativeInstallationRoot.Error(id, message, path, details);
}
