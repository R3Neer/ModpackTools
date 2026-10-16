using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

public sealed record NativePublishedVersion(NativePreparedPackage Package, bool Published);

public static class NativeVersionPublication
{
    // The caller owns this version-store directory and retains the installation's
    // admission lease. This additional physical store lease also serializes direct
    // callers. Publishing a version never changes the active control generation.
    public static async Task<NativePublishedVersion> PublishAsync(NativePreparedPackage prepared,
        ProjectPhysicalIdentity versionStore, NativePackageLimits? limits = null, CancellationToken cancellation = default,
        Action<NativePackageProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(versionStore);
        limits ??= new(); NativePackageArchive.Verify(prepared, limits, cancellation);
        using var source = WindowsProjectIdentity.Open(prepared.Directory);
        if (source.Value != prepared.Identity) throw Error("PreparedChanged", "Prepared package directory identity changed", prepared.Directory);
        using var parent = WindowsProjectIdentity.Open(versionStore.CanonicalRoot);
        if (parent.Value != versionStore) throw Error("StoreChanged", "Version-store directory identity changed", versionStore.CanonicalRoot);
        var root = parent.Value.CanonicalRoot;
        if (StringComparer.OrdinalIgnoreCase.Equals(source.Value.CanonicalRoot, root)
            || TransactionFiles.IsBelow(source.Value.CanonicalRoot, root) || TransactionFiles.IsBelow(root, source.Value.CanonicalRoot))
            throw Error("UnsafeDestination", "Prepared package and version store must be separate trees", root);
        await using var lease = await NamedProjectLease.AcquireAsync(versionStore.FileIdentity).ConfigureAwait(false);
        var target = TransactionFiles.Within(root, prepared.Manifest.BuildId);
        if (Directory.Exists(target))
        {
            var existing = prepared with { Directory = target, Identity = WindowsProjectIdentity.Inspect(target) };
            NativePackageArchive.Verify(existing, limits, cancellation); parent.AssertCurrent();
            return new(existing, false);
        }
        if (File.Exists(target)) throw Error("VersionConflict", "Version destination is occupied by a file", target);
        cancellation.ThrowIfCancellationRequested();
        var candidate = Path.Combine(root, ".modpacktools-publish-" + Guid.NewGuid().ToString("N"));
        if (!WindowsDirectoryReservation.TryCreate(candidate)) throw Error("StageCreateFailed", "Version publication stage could not be reserved", candidate);
        string? stage = candidate; ProjectPhysicalIdentity? stageIdentity = null;
        var owned = new Dictionary<string, NativePackageFile?>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            NativePublishedVersion result;
            using (var identity = WindowsProjectIdentity.Open(stage))
            {
                stageIdentity = identity.Value; long completed = 0; var count = 0;
                var total = prepared.Files.Sum(file => file.Length);
                foreach (var file in prepared.Files)
                {
                    cancellation.ThrowIfCancellationRequested(); source.AssertCurrent(); identity.AssertCurrent(); parent.AssertCurrent();
                    NativePackageArchive.CreateDirectories(stage, file.Path, directories);
                    var destinationPath = TransactionFiles.Within(stage, file.Path); owned.Add(file.Path, null);
                    string digest;
                    using (var input = new FileStream(TransactionFiles.Within(prepared.Directory, file.Path), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
                    using (var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, file.Length, file.Path, defaultStreamOnly: true);
                        digest = NativePackageArchive.Copy(input, output, file.Length, cancellation,
                            bytes => progress?.Invoke(new("publish-copy", destinationPath, completed + bytes, total, count, prepared.Files.Length)));
                        output.Flush(flushToDisk: true);
                    }
                    owned[file.Path] = file with { Sha256 = digest };
                    if (digest != file.Sha256) throw Error("PreparedChanged", "Prepared payload changed during publication", file.Path);
                    completed += file.Length; count++;
                    progress?.Invoke(new("publish-copied", destinationPath, completed, total, count, prepared.Files.Length));
                }
                var staged = prepared with { Directory = stage, Identity = identity.Value };
                NativePackageArchive.Verify(staged, limits, cancellation);
                NativePackageArchive.Verify(prepared, limits, cancellation);
                parent.AssertCurrent(); identity.AssertCurrent(); cancellation.ThrowIfCancellationRequested();
                // Materialize the receipt before the indivisible same-parent move.
                // File identity survives that move; no fallible read/callback after it.
                result = new(prepared with { Directory = target, Identity = new(target, identity.Value.FileIdentity) }, true);
            }
            Directory.Move(stage, target); stage = null; return result;
        }
        catch (Exception error)
        {
            if (stage is not null)
            {
                try { NativePackageArchive.Cleanup(stage, stageIdentity, owned, directories, limits); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
                {
                    var retained = Error("PublicationStageRetained", "Version publication failed; inspect its retained stage", stage, error.Message + "; " + cleanup.Message);
                    retained.Data["ModpackTools.RetainStage"] = true; throw retained;
                }
            }
            if (error is OperationCanceledException) throw new DomainException("Core.OperationCancelled", "Version publication was cancelled", "retry when ready", "OperationStopped", root);
            throw;
        }
    }

    private static DomainException Error(string id, string message, string path, string? details = null) => NativePackageManifest.Error(id, message, path, details);
}
