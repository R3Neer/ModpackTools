using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Transactions;

// Domain adapter only: directory ownership, recovery and publication are shared
// with package installation; project-context overrides remain caller-owned.
public static class ProjectTransaction
{
    public static ProjectPhysicalIdentity InspectIdentity(string root) => FileTreeTransaction.InspectIdentity(root);
    internal static string CanonicalFutureDirectory(string path) => FileTreeTransaction.CanonicalFutureDirectory(path);

    public static Task<TransactionResult<T>> ExecuteAsync<T>(ProjectContext original, string stateDirectory,
        Func<ProjectContext, CancellationToken, Task<T>> prepareStage,
        Func<ProjectContext, CancellationToken, Task>? verifyStage = null, bool dryRun = false,
        TransactionExpectation? expected = null, TransactionLimits? limits = null, CancellationToken cancellation = default,
        Func<TransactionEvent, ValueTask>? observer = null,
        Func<TransactionPreview<T>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(prepareStage);
        ProjectContext? stagedContext = null;
        return FileTreeTransaction.ExecuteAsync(original.Root, stateDirectory,
            (stage, token) => prepareStage(Context(stage), token),
            verifyStage is null ? null : (stage, token) => verifyStage(Context(stage), token),
            dryRun, expected, limits, cancellation, observer, prepareOutput);

        ProjectContext Context(string stage) => stagedContext ??= original with { Root = stage, IndexPath = TransactionFiles.Within(stage, original.IndexFile) };
    }
}
