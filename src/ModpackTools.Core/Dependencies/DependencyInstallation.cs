using System.Collections.Immutable;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Source;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Dependencies;

public sealed record DependencyInstallation(string Path, string Version, bool Installed, ImmutableArray<TransactionDiagnostic> Diagnostics);

public static class DependencyInstaller
{
    public static async Task<DependencyInstallation> InstallAsync(PreparedDependency prepared, string targetDirectory, string stateDirectory,
        DependencyPackage package, string nativeHost, CancellationToken cancellation = default,
        Func<TransactionEvent, ValueTask>? observer = null)
    {
        try { return await InstallCoreAsync(prepared, targetDirectory, stateDirectory, package, nativeHost, cancellation, observer).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Dependency installation was cancelled", "inspect retained transaction state before retrying", "OperationStopped", targetDirectory); }
    }
    private static async Task<DependencyInstallation> InstallCoreAsync(PreparedDependency prepared, string targetDirectory, string stateDirectory,
        DependencyPackage package, string nativeHost, CancellationToken cancellation, Func<TransactionEvent, ValueTask>? observer)
    {
        ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(package);
        if (prepared.Version != package.Version || prepared.Files.IsDefault || prepared.Files.Length != 2
            || prepared.Files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2
            || prepared.Files.Any(file => file.Name != package.Executable && file.Name != package.License || file.Length is <= 0 or > 256 * 1024 * 1024
                || file.Sha256.Length != 64 || file.Sha256.Any(value => !Uri.IsHexDigit(value)))
            || prepared.Files.SingleOrDefault(file => file.Name == package.Executable) is not { } executable
            || !StringComparer.OrdinalIgnoreCase.Equals(executable.Sha256, package.ExecutableSha256))
            throw Error("PreparedPackageInvalid", "Prepared dependency does not match its trusted metadata", prepared.Directory);
        cancellation.ThrowIfCancellationRequested();
        using var sourceIdentity = WindowsProjectIdentity.Open(prepared.Directory);
        var sourceDirectory = sourceIdentity.Value.CanonicalRoot;
        targetDirectory = FileTreeTransaction.CanonicalFutureDirectory(targetDirectory);
        stateDirectory = FileTreeTransaction.CanonicalFutureDirectory(stateDirectory);
        if (StringComparer.OrdinalIgnoreCase.Equals(targetDirectory, sourceDirectory) || TransactionFiles.IsBelow(sourceDirectory, targetDirectory) || TransactionFiles.IsBelow(targetDirectory, sourceDirectory))
            throw Error("UnsafeDestination", "Prepared and installed dependency trees must be separate", targetDirectory);
        if (StringComparer.OrdinalIgnoreCase.Equals(stateDirectory, sourceDirectory) || TransactionFiles.IsBelow(stateDirectory, sourceDirectory) || TransactionFiles.IsBelow(sourceDirectory, stateDirectory))
            throw Error("UnsafeDestination", "Prepared dependency and recovery state trees must be separate", stateDirectory);
        Verify(sourceDirectory, cancellation);
        WindowsProjectIdentity.GuardAncestors(targetDirectory); Directory.CreateDirectory(targetDirectory); WindowsProjectIdentity.GuardAncestors(targetDirectory);
        var tree = new SourceTreeReadLimits(MaximumFiles: 256, MaximumDirectories: 64, MaximumBytes: 512 * 1024 * 1024, MaximumPathCharacters: 64 * 1024);
        var result = await FileTreeTransaction.ExecuteAsync(targetDirectory, stateDirectory, Prepare, VerifyStage,
            limits: new(Tree: tree, RecoveryTree: tree), cancellation: cancellation, observer: observer).ConfigureAwait(false);
        return new(Path.Combine(targetDirectory, package.Executable), package.Version, result.Applied, result.Diagnostics);

        async Task<bool> Prepare(string stage, CancellationToken token)
        {
            sourceIdentity.AssertCurrent(); Verify(sourceDirectory, token);
            foreach (var file in prepared.Files)
                TransactionFiles.CopyVerified(TransactionFiles.Within(sourceDirectory, file.Name), TransactionFiles.Within(stage, file.Name), file.Sha256.ToUpperInvariant(), file.Length, token);
            Verify(stage, token);
            var beforeSmoke = SourceFingerprint.All(SourceTree.Read(stage, tree, token));
            _ = await NativeTool.RunAsync(TransactionFiles.Within(stage, package.Executable), ["--help"], stage, nativeHost, new(10000, 64 * 1024), token).ConfigureAwait(false);
            if (SourceFingerprint.All(SourceTree.Read(stage, tree, token)) != beforeSmoke)
                throw Error("InvocationChangedPackage", "Dependency invocation changed its prepared package", stage);
            Verify(stage, token); return true;
        }
        Task VerifyStage(string stage, CancellationToken token)
        { sourceIdentity.AssertCurrent(); Verify(sourceDirectory, token); Verify(stage, token); return Task.CompletedTask; }
        void Verify(string root, CancellationToken token)
        {
            foreach (var file in prepared.Files)
            {
                token.ThrowIfCancellationRequested(); var path = TransactionFiles.Within(root, file.Name);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Length || !StringComparer.OrdinalIgnoreCase.Equals(TransactionFiles.Hash(path, file.Length, token), file.Sha256))
                    throw Error("PreparedPackageChanged", "Prepared dependency file is missing or changed", path);
            }
        }
    }
    private static DomainException Error(string id, string message, string path) => new("Dependency." + id, message, "prepare the verified package again before installing", "InvalidData", path);
}
