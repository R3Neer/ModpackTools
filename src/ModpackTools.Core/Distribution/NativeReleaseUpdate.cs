using ModpackTools.Core.Dependencies;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

public static class NativeReleaseUpdate
{
    // Selection/confirmation belongs to the caller. This composes the same verified
    // installation path for an already selected official release, including repair
    // of a same-public-version package in controlled deployments.
    public static async Task<NativeInstallationResult> ApplyAsync(string root, string stateDirectory, string nativeHost,
        NativeRelease release, string expectedGeneration, CancellationToken cancellation = default,
        Action<NativePackageProgress>? progress = null, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        var asset = release.Asset ?? throw Error("MissingNativeAsset", "The selected release has no native Windows x64 package", release.Url);
        // Public records are values, not authentication. Revalidate the complete
        // selected identity before letting any field become a download authority.
        var expectedName = "ModpackTools-Native-win-x64-" + release.Version + ".zip";
        if (release.Tag != "v" + release.Version || !Version.TryParse(release.Version, out var version) || version.Build < 0 || version.Revision >= 0 || version.ToString() != release.Version
            || release.Url != "https://github.com/R3Neer/ModpackTools/releases/tag/" + release.Tag
            || asset.Name != expectedName || asset.Url != "https://github.com/R3Neer/ModpackTools/releases/download/" + release.Tag + "/" + expectedName
            || !NativePackageManifest.HashValue(asset.Sha256) || asset.Length is <= 0 or > 256L * 1024 * 1024)
            throw Error("InvalidAsset", "Selected native release identity is invalid", release.Url);
        if (!Guid.TryParseExact(expectedGeneration, "N", out _)) throw Error("InvalidGeneration", "Update requires a selected installation generation", root);
        await using (var admission = await NativeInstallationRoot.AcquireAsync(root).ConfigureAwait(false))
        {
            root = admission.Owner.Root.CanonicalRoot;
            if (NativeInstallationControl.Read(Path.Combine(root, "control"))?.Generation != expectedGeneration)
                throw Error("GenerationChanged", "Installation changed before its update download started", root);
        }
        using var temporary = WindowsProjectIdentity.Open(FileTreeTransaction.CanonicalFutureDirectory(Path.GetTempPath()));
        if (temporary.Value.CanonicalRoot.Equals(root, StringComparison.OrdinalIgnoreCase) || TransactionFiles.IsBelow(temporary.Value.CanonicalRoot, root))
            throw Error("UnsafePreparation", "Update download must remain outside the application tree", temporary.Value.CanonicalRoot);
        DownloadedDependency? downloaded = null; NativeInstallationResult? result = null; Exception? primary = null;
        try
        {
            try
            {
                downloaded = await DependencyDownload.FetchAsync(new(asset.Url), asset.Sha256, temporary.Value.CanonicalRoot,
                    limits: new(MaximumBytes: asset.Length), cancellation: cancellation, handler: handler,
                    progress: completed => progress?.Invoke(new("download", asset.Url, completed, asset.Length, 0, 1))).ConfigureAwait(false);
            }
            catch (DomainException error) when (error.Id.StartsWith("ModpackTools.Dependency.", StringComparison.Ordinal))
            {
                var mapped = Error("DownloadFailed", "The verified native ModpackTools package could not be downloaded", error.Target is TextTarget target ? target.Value : asset.Url,
                    error.Message.Replace("Packwiz", "ModpackTools", StringComparison.Ordinal) + (error.Details is null ? "" : "; " + error.Details));
                foreach (var key in error.Data.Keys) mapped.Data[key] = error.Data[key]; throw mapped;
            }
            temporary.AssertCurrent();
            if (downloaded.Length != asset.Length) throw Error("AssetLengthMismatch", "Downloaded native package length differs from the selected release", asset.Url);
            result = await NativeInstallation.ApplyAsync(downloaded.Path, asset.Sha256, root, null, stateDirectory, nativeHost,
                install: false, cancellation: cancellation, progress: progress, expectedGeneration: expectedGeneration,
                expectedVersion: release.Version).ConfigureAwait(false);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (downloaded is not null)
            {
                try
                {
                    temporary.AssertCurrent(); WindowsProjectIdentity.GuardAncestors(downloaded.Path);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(downloaded.Path), temporary.Value.CanonicalRoot))
                        throw Error("DownloadChanged", "Downloaded archive escaped its owned temporary parent", downloaded.Path);
                    using (var input = new FileStream(downloaded.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, downloaded.Length, downloaded.Path, defaultStreamOnly: true);
                        if (input.Length != downloaded.Length || NativePackageArchive.Copy(input, Stream.Null, downloaded.Length, CancellationToken.None) != downloaded.Sha256)
                            throw Error("DownloadChanged", "Downloaded archive changed before cleanup", downloaded.Path);
                    }
                    File.Delete(downloaded.Path);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
                {
                    if (result is not null)
                    {
                        var diagnostic = new TransactionDiagnostic("SelfUpdate.DownloadCleanupPending", downloaded.Path, cleanup.Message);
                        result = result.Diagnostics.Length < 8 ? result with { Diagnostics = result.Diagnostics.Add(diagnostic) }
                            : result with { DiagnosticsOmitted = result.DiagnosticsOmitted + 1 };
                    }
                    else
                    {
                        var retained = Error("DownloadRetained", "Update failed; preserve the downloaded native package for inspection", downloaded.Path,
                            (primary is DomainException domain ? domain.Id + ": " + domain.Message
                                + (domain.Target is TextTarget target ? "; target=" + target.Value : "")
                                + (domain.Details is null ? "" : "; details=" + domain.Details) : primary?.Message) + "; " + cleanup.Message);
                        if (primary is not null) foreach (var key in primary.Data.Keys) retained.Data[key] = primary.Data[key];
                        retained.Data["ModpackTools.RetainStage"] = true; throw retained;
                    }
                }
            }
        }
        return result!;
    }

    private static DomainException Error(string id, string message, string path, string? details = null) =>
        new("SelfUpdate." + id, message, "inspect the selected official native package and retry", "InvalidData", path, details: details);
}
