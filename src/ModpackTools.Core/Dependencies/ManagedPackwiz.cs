using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Dependencies;

public sealed record DependencyBootstrapSource(DependencyPackage Package, Uri Uri)
{
    public static DependencyBootstrapSource Packwiz { get; } = new(DependencyPackage.Packwiz, DependencyPackage.PackwizUri);
}

public static class ManagedPackwiz
{
    public static Task<DependencyInstallation> InstallAsync(string configurationDirectory, string stateDirectory, string temporaryParent,
        string nativeHost, string? archivePath = null, CancellationToken cancellation = default)
        => DependencyBootstrap.InstallAsync(DependencyBootstrapSource.Packwiz, configurationDirectory, stateDirectory, temporaryParent,
            nativeHost, archivePath, cancellation);
}

// The source is caller-trusted release metadata. The public Packwiz entry point
// above selects its pin; test inspectors can supply their isolated fixture pin.
public static class DependencyBootstrap
{
    public static async Task<DependencyInstallation> InstallAsync(DependencyBootstrapSource source, string configurationDirectory,
        string stateDirectory, string temporaryParent, string nativeHost, string? archivePath = null, CancellationToken cancellation = default,
        HttpMessageHandler? handler = null, Func<TransactionEvent, ValueTask>? observer = null)
    {
        try { return await InstallCoreAsync(source, configurationDirectory, stateDirectory, temporaryParent, nativeHost, archivePath, cancellation, handler, observer).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Managed dependency preparation was cancelled", "retry when ready", "OperationStopped", configurationDirectory); }
    }
    private static async Task<DependencyInstallation> InstallCoreAsync(DependencyBootstrapSource source, string configurationDirectory,
        string stateDirectory, string temporaryParent, string nativeHost, string? archivePath, CancellationToken cancellation,
        HttpMessageHandler? handler, Func<TransactionEvent, ValueTask>? observer)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(source.Package);
        if (string.IsNullOrWhiteSpace(source.Package.Version) || source.Package.Version.Length > 64
            || Path.GetFileName(source.Package.Version) != source.Package.Version)
            throw new DomainException("Dependency.InvalidManifest", "Managed dependency version is not a safe directory name", "use the pinned release manifest");
        cancellation.ThrowIfCancellationRequested();
        configurationDirectory = FileTreeTransaction.CanonicalFutureDirectory(configurationDirectory);
        stateDirectory = FileTreeTransaction.CanonicalFutureDirectory(stateDirectory);
        temporaryParent = FileTreeTransaction.CanonicalFutureDirectory(temporaryParent);
        var target = FileTreeTransaction.CanonicalFutureDirectory(TransactionFiles.Within(configurationDirectory, "tools/packwiz/" + source.Package.Version));
        WindowsProjectIdentity.GuardAncestors(temporaryParent); Directory.CreateDirectory(temporaryParent);
        using var parentIdentity = WindowsProjectIdentity.Open(temporaryParent);
        var workspace = Path.Combine(parentIdentity.Value.CanonicalRoot, ".modpacktools-bootstrap-" + Guid.NewGuid().ToString("N"));
        foreach (var path in new[] { stateDirectory, target })
            if (StringComparer.OrdinalIgnoreCase.Equals(path, workspace) || TransactionFiles.IsBelow(path, workspace) || TransactionFiles.IsBelow(workspace, path))
                throw new DomainException("Dependency.UnsafeDestination", "Dependency preparation and permanent/recovery paths must be separate", "choose separate temporary, installation and recovery locations", target: path);
        if (!WindowsDirectoryReservation.TryCreate(workspace))
            throw new DomainException("Dependency.StageCreateFailed", "Dependency preparation directory could not be reserved", "choose a writable temporary directory", target: workspace);
        DownloadedDependency? download = null; PreparedDependency? prepared = null;
        DependencyInstallation installation;
        try
        {
            var packagePath = archivePath;
            if (packagePath is null)
            {
                download = await DependencyDownload.FetchAsync(source.Uri, source.Package.ArchiveSha256, workspace, cancellation: cancellation, handler: handler).ConfigureAwait(false);
                packagePath = download.Path;
            }
            prepared = DependencyArchive.Prepare(packagePath, workspace, source.Package, cancellation: cancellation);
            installation = await DependencyInstaller.InstallAsync(prepared, target, stateDirectory, source.Package, nativeHost, cancellation, observer).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (error.Data["ModpackTools.RetainStage"] is true) throw Retain(error.Message, error is DomainException domain ? domain.Details : null);
            try { Cleanup(); }
            catch (Exception cleanup) when (Recoverable(cleanup)) { throw Retain(error.Message, cleanup.Message); }
            if (error is OperationCanceledException) throw new DomainException("Core.OperationCancelled", "Managed dependency preparation was cancelled", "retry when ready", "OperationStopped", workspace);
            if (error is IOException or UnauthorizedAccessException)
                throw new DomainException("Dependency.PreparationFailed", "Managed dependency could not be prepared", "check the package and temporary paths before retrying", "ReadError", archivePath ?? workspace, details: error.Message);
            throw;
        }
        try { Cleanup(); }
        catch (Exception cleanup) when (Recoverable(cleanup))
        { installation = installation with { Diagnostics = installation.Diagnostics.Add(new("Dependency.CleanupFailed", workspace, cleanup.Message)) }; }
        return installation;

        void Cleanup()
        {
            parentIdentity.AssertCurrent(); WindowsProjectIdentity.GuardAncestors(workspace);
            var expectedRoot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (download is not null) expectedRoot.Add(download.Path);
            if (prepared is not null) expectedRoot.Add(prepared.Directory);
            var rootEntries = Directory.EnumerateFileSystemEntries(workspace).Take(4).ToArray();
            if (rootEntries.Any(path => !expectedRoot.Contains(path))) throw new IOException("Preparation directory contains unexpected data; retained: " + workspace);
            if (download is not null) VerifyFile(download.Path, download.Sha256, download.Length);
            if (prepared is not null)
            {
                WindowsProjectIdentity.GuardAncestors(prepared.Directory);
                var expectedFiles = prepared.Files.Select(file => TransactionFiles.Within(prepared.Directory, file.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (Directory.EnumerateFileSystemEntries(prepared.Directory).Take(4).Any(path => !expectedFiles.Contains(path)))
                    throw new IOException("Prepared dependency contains unexpected data; retained: " + prepared.Directory);
                foreach (var file in prepared.Files) VerifyFile(TransactionFiles.Within(prepared.Directory, file.Name), file.Sha256, file.Length);
            }
            // Verify the complete owned set before deleting its first leaf.
            if (prepared is not null)
            { foreach (var file in prepared.Files) File.Delete(TransactionFiles.Within(prepared.Directory, file.Name)); Directory.Delete(prepared.Directory, recursive: false); }
            if (download is not null) File.Delete(download.Path);
            Directory.Delete(workspace, recursive: false);
        }
        static void VerifyFile(string path, string hash, long length)
        {
            WindowsProjectIdentity.GuardAncestors(path);
            if (!File.Exists(path) || new FileInfo(path).Length != length || !StringComparer.OrdinalIgnoreCase.Equals(TransactionFiles.Hash(path, length), hash))
                throw new IOException("Owned preparation file changed; retained: " + path);
        }
        DomainException Retain(string message, string? detail)
        {
            var retained = new DomainException("Dependency.BootstrapRetained", "Dependency preparation failed; temporary evidence was retained", "inspect the retained preparation and recovery state before retrying", "InvalidOperation", workspace, details: message + (detail is null ? "" : "; " + detail));
            retained.Data["ModpackTools.RetainStage"] = true; return retained;
        }
    }
    private static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException or DomainException;
}
