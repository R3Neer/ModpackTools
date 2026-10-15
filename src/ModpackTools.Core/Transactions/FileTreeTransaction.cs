using System.Collections.Immutable;
using ModpackTools.Core.Source;

namespace ModpackTools.Core.Transactions;

public static partial class FileTreeTransaction
{
    public static ProjectPhysicalIdentity InspectIdentity(string root) => WindowsProjectIdentity.Inspect(root);

    // stateDirectory is a stable recovery namespace, not a per-invocation cache
    // option. Changing it requires migrating pending journals first. Global ownership
    // serializes physical roots across namespaces; it cannot discover another store.
    public static async Task<TransactionResult<T>> ExecuteAsync<T>(string originalRoot, string stateDirectory,
        Func<string, CancellationToken, Task<T>> prepareStage,
        Func<string, CancellationToken, Task>? verifyStage = null, bool dryRun = false,
        TransactionExpectation? expected = null, TransactionLimits? limits = null, CancellationToken cancellation = default,
        Func<TransactionEvent, ValueTask>? observer = null,
        Func<TransactionPreview<T>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalRoot); ArgumentNullException.ThrowIfNull(prepareStage);
        limits ??= new(); ValidatePolicy(limits);
        if (expected is not null && (expected.Project is null || expected.Changes.IsDefault || expected.Changes.Length > limits.MaximumChanges || string.IsNullOrEmpty(expected.BeforeFingerprint)))
            throw WindowsProjectIdentity.Error("InvalidExpectation", "Reviewed transaction expectation is incomplete");
        var diagnostics = ImmutableArray.CreateBuilder<TransactionDiagnostic>();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var identity = WindowsProjectIdentity.Open(originalRoot);
            var root = identity.Value.CanonicalRoot;
            var state = CanonicalFutureDirectory(stateDirectory);
            if (StringComparer.OrdinalIgnoreCase.Equals(state, root) || TransactionFiles.IsBelow(state, root))
                throw WindowsProjectIdentity.Error("UnsafeStateDirectory", "Transaction state must be outside the protected project", state);
            await using var lease = await NamedProjectLease.AcquireAsync(identity.Value.FileIdentity).ConfigureAwait(false);
            var home = Path.Combine(state, "native-transactions", "v1", identity.Value.FileIdentity);
            WindowsProjectIdentity.GuardAncestors(home); Directory.CreateDirectory(home); WindowsProjectIdentity.GuardAncestors(home);
            var legacyHome = Path.Combine(state, "transactions", TextHash.Compute(root.ToLowerInvariant()));
            WindowsProjectIdentity.GuardAncestors(legacyHome); Directory.CreateDirectory(legacyHome); WindowsProjectIdentity.GuardAncestors(legacyHome);
            using var legacyLock = OpenLegacyLock(Path.Combine(legacyHome, "lock"));
            await Notify(TransactionPhase.LeaseAcquired, home).ConfigureAwait(false);
            // Recovery has its own budget and never uses an already cancelled
            // forward token. Pre-cancellation was checked before acquiring ownership.
            RecoverAll(home, identity, limits, diagnostics);
            _ = SourceTree.Read(root, limits.RecoveryTree ?? new());
            await Notify(TransactionPhase.RecoveryCompleted, home).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); identity.AssertCurrent();
            var treeLimits = limits.Tree ?? new(); var before = SourceTree.Read(root, treeLimits, cancellation);
            var fingerprint = SourceFingerprint.All(before);
            if (expected is not null && (expected.Project != identity.Value || !StringComparer.Ordinal.Equals(expected.BeforeFingerprint, fingerprint)))
                throw WindowsProjectIdentity.Error("PlanChanged", "The protected source differs from the reviewed transaction", root);
            var workspace = Path.Combine(home, Guid.NewGuid().ToString("N"));
            if (Directory.Exists(workspace) || File.Exists(workspace)) throw WindowsProjectIdentity.Error("WorkspaceCollision", "Transaction workspace already exists", workspace);
            Directory.CreateDirectory(workspace); var owner = new TransactionOwner(Path.GetFileName(workspace), identity.Value, "preparing");
            TransactionJournalStore.WriteOwner(workspace, owner, limits);
            var retainStage = false;
            try
            {
                var stage = Path.Combine(workspace, "stage"); Directory.CreateDirectory(stage); var completed = 0;
                foreach (var (relative, hash) in before.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    TransactionFiles.CopyVerified(TransactionFiles.Within(root, relative), TransactionFiles.Within(stage, relative), hash, treeLimits.MaximumBytes, cancellation);
                    await Notify(TransactionPhase.StageFileCopied, workspace, relative, ++completed).ConfigureAwait(false);
                }
                AssertEqual(before, SourceTree.Read(stage, treeLimits, cancellation), "The project changed while it was copied");
                await Notify(TransactionPhase.StageCopied, workspace).ConfigureAwait(false);
                var result = await prepareStage(stage, cancellation).ConfigureAwait(false);
                await Notify(TransactionPhase.Prepared, workspace).ConfigureAwait(false);
                if (verifyStage is not null) await verifyStage(stage, cancellation).ConfigureAwait(false);
                await Notify(TransactionPhase.Verified, workspace).ConfigureAwait(false);
                var after = SourceTree.Read(stage, treeLimits, cancellation); var changes = SourceTreeChanges.Compare(before, after);
                var exactBeforeNames = before.Files.Keys.ToHashSet(StringComparer.Ordinal);
                foreach (var path in after.Files.Keys)
                    if (before.Files.ContainsKey(path) && !exactBeforeNames.Contains(path))
                        throw WindowsProjectIdentity.Error("UnsupportedTopology", "Case-only path renames are not supported by this transaction", path);
                if (changes.Length > limits.MaximumChanges) throw Resource(nameof(limits.MaximumChanges));
                identity.AssertCurrent(); AssertEqual(before, SourceTree.Read(root, treeLimits, cancellation), "The project changed during preparation; nothing was applied");
                if (expected is not null && !expected.Changes.SequenceEqual(changes))
                    throw WindowsProjectIdentity.Error("PlanChanged", "The transaction write set differs from the reviewed plan", root);
                var expectation = new TransactionExpectation(identity.Value, fingerprint, changes);
                foreach (var path in before.Files.Keys)
                    if (Directory.Exists(TransactionFiles.Within(stage, path))) throw WindowsProjectIdentity.Error("UnsupportedTopology", "A protected file became a staged directory", path);
                var directories = CreatedDirectories(root, changes, treeLimits);
                // Materialize the owned public result before publishing pending;
                // output quotas/serialization must not fail after applying a plan.
                if (prepareOutput is not null) await prepareOutput(new(result, changes, expectation), cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (dryRun || changes.IsEmpty)
                {
                    CleanupCompleted(home, workspace, identity.Value, limits, diagnostics);
                    return new(result, changes, false, expectation, diagnostics.ToImmutable());
                }
                var backupRoot = Path.Combine(workspace, "backup"); completed = 0;
                foreach (var change in changes)
                {
                    _ = TransactionFiles.Within(root, change.Path); _ = TransactionFiles.Within(stage, change.Path);
                    if (change.Before is null) continue;
                    TransactionFiles.CopyVerified(TransactionFiles.Within(root, change.Path), TransactionFiles.Within(backupRoot, change.Path), change.Before, treeLimits.MaximumBytes, cancellation);
                    await Notify(TransactionPhase.BackupFileCopied, workspace, change.Path, ++completed).ConfigureAwait(false);
                }
                ValidateBackups(workspace, changes, treeLimits.MaximumBytes);
                await Notify(TransactionPhase.BackedUp, workspace).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); identity.AssertCurrent();
                AssertEqual(before, SourceTree.Read(root, treeLimits, cancellation), "The project changed before journal publication");
                var temporaries = ImmutableArray.CreateBuilder<string>();
                for (var index = 0; index < changes.Length; index++)
                {
                    if (changes[index].After is not null) temporaries.Add(TransactionFiles.PublicationRelative(changes[index].Path, owner.TransactionId, index, false));
                    if (changes[index].Before is not null) temporaries.Add(TransactionFiles.PublicationRelative(changes[index].Path, owner.TransactionId, index, true));
                }
                foreach (var temporary in temporaries)
                    if (File.Exists(TransactionFiles.Within(root, temporary)) || Directory.Exists(TransactionFiles.Within(root, temporary)))
                        throw WindowsProjectIdentity.Error("TemporaryCollision", "Reserved publication temporary already exists", temporary);
                var journal = new TransactionJournal(owner, fingerprint, changes, directories, temporaries.ToImmutable(), "pending");
                TransactionJournalStore.WriteJournal(workspace, journal, limits);
                // Both records are durable before ANY protected publication. A missing
                // journal with pending owner is retained, never guessed safe to delete.
                owner = owner with { Status = "pending" }; TransactionJournalStore.WriteOwner(workspace, owner, limits);
                try
                {
                    await Notify(TransactionPhase.JournalWritten, workspace).ConfigureAwait(false); completed = 0;
                    foreach (var change in changes)
                    {
                        cancellation.ThrowIfCancellationRequested(); identity.AssertCurrent();
                        var destination = TransactionFiles.Within(root, change.Path);
                        if (TransactionFiles.Hash(destination, treeLimits.MaximumBytes, cancellation) != change.Before)
                            throw WindowsProjectIdentity.Error("ConcurrentChange", "A destination changed during commit", change.Path);
                        if (change.After is not null) TransactionFiles.CopyVerified(TransactionFiles.Within(stage, change.Path), destination, change.After, treeLimits.MaximumBytes, cancellation,
                            TransactionFiles.Within(root, TransactionFiles.PublicationRelative(change.Path, owner.TransactionId, completed, false)));
                        else File.Delete(destination);
                        await Notify(TransactionPhase.FileCommitted, workspace, change.Path, ++completed).ConfigureAwait(false);
                    }
                    AssertEqual(after, SourceTree.Read(root, treeLimits, cancellation), "Written state differs from the prepared state", "VerificationFailed");
                    await Notify(TransactionPhase.StateVerified, workspace).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    TransactionJournalStore.WriteJournal(workspace, journal with { Status = "committed" }, limits);
                }
                catch (Exception primary)
                {
                    // Recovery reserves the original maximum file budget, irrespective
                    // of a spent forward token; failure leaves all durable evidence.
                    try { Restore(workspace, journal, identity, Math.Max(treeLimits.MaximumBytes, (limits.RecoveryTree ?? new()).MaximumBytes), limits); }
                    catch (Exception recovery)
                    {
                        var path = Path.Combine(workspace, "journal.json");
                        throw WindowsProjectIdentity.Error("RecoveryFailed", "Transaction failed and recovery could not complete; evidence was preserved", path,
                            "Primary: " + FailureSummary(primary) + Environment.NewLine + "Recovery: " + FailureSummary(recovery));
                    }
                    throw;
                }
                // Past the committed marker, notification/cleanup cannot roll back or
                // report an unapplied result. Their failures are returned diagnostics.
                try { await Notify(TransactionPhase.Committed, workspace).ConfigureAwait(false); }
                catch (Exception error) { diagnostics.Add(new("Transaction.CommittedNotificationFailed", workspace, error.Message)); }
                try
                {
                    owner = owner with { Status = "committed" }; TransactionJournalStore.WriteOwner(workspace, owner, limits);
                    File.Delete(Path.Combine(workspace, "journal.json"));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or DomainException)
                { diagnostics.Add(new("Transaction.CommittedCleanupPending", workspace, error.Message)); }
                CleanupCompleted(home, workspace, identity.Value, limits, diagnostics);
                return new(result, changes, true, expectation, diagnostics.ToImmutable());
            }
            catch (Exception error) when (error.Data["ModpackTools.RetainStage"] is true)
            {
                // A native tool owner could not prove all descendants stopped.
                // Preserve the stage instead of racing a possibly active writer.
                retainStage = true;
                try { TransactionJournalStore.WriteOwner(workspace, owner with { Status = "retained" }, limits); }
                catch (Exception marker) when (marker is IOException or UnauthorizedAccessException or DomainException)
                {
                    var failure = WindowsProjectIdentity.Error("StageRetentionMarkerFailed", "Stage was preserved but its retention marker could not be saved", workspace,
                        "Primary: " + FailureSummary(error) + Environment.NewLine + "Retention marker: " + FailureSummary(marker));
                    failure.Data["ModpackTools.RetainStage"] = true; throw failure;
                }
                throw;
            }
            finally
            {
                if (!retainStage) CleanupCompleted(home, workspace, identity.Value, limits, diagnostics);
            }
        }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Project transaction was cancelled", "inspect any retained recovery state before retrying", "OperationStopped"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw WindowsProjectIdentity.Error("IoFailed", "Project transaction IO failed", originalRoot, error.Message); }

        ValueTask Notify(TransactionPhase phase, string workspace, string? relative = null, int completed = 0) =>
            observer is null ? ValueTask.CompletedTask : observer(new(phase, workspace, relative, completed));
    }

    private static void ValidatePolicy(TransactionLimits limits)
    {
        var invalid = limits.MaximumWorkspaces <= 0 ? nameof(limits.MaximumWorkspaces)
            : limits.MaximumJournalBytes is <= 0 or > 256 * 1024 * 1024 ? nameof(limits.MaximumJournalBytes)
            : limits.MaximumChanges <= 0 ? nameof(limits.MaximumChanges)
            : limits.MaximumCleanupEntries <= 0 ? nameof(limits.MaximumCleanupEntries) : null;
        foreach (var tree in new[] { limits.Tree ?? new(), limits.RecoveryTree ?? new() })
            if (tree.MaximumBytes <= 0 || tree.MaximumDirectories <= 0 || tree.MaximumFiles <= 0 || tree.MaximumPathCharacters <= 0) invalid ??= "Tree";
        if (invalid is not null) throw new DomainException("Transaction.InvalidPolicy", "Transaction resource limits are invalid", "use positive bounded quotas", target: invalid);
    }
    internal static string CanonicalFutureDirectory(string path)
    {
        var absolute = Path.GetFullPath(path); WindowsProjectIdentity.GuardAncestors(absolute); var suffix = new Stack<string>();
        var current = absolute;
        while (!Directory.Exists(current))
        {
            if (File.Exists(current)) throw WindowsProjectIdentity.Error("UnsafeStateDirectory", "Transaction state path is occupied by a file", current);
            suffix.Push(Path.GetFileName(current)); current = Path.GetDirectoryName(current) ?? throw WindowsProjectIdentity.Error("UnsafeStateDirectory", "State directory has no existing ancestor", absolute);
        }
        using var ancestor = WindowsProjectIdentity.Open(current); var result = ancestor.Value.CanonicalRoot;
        foreach (var part in suffix) result = Path.Combine(result, part); return result;
    }
    private static FileStream OpenLegacyLock(string path)
    {
        WindowsProjectIdentity.GuardAncestors(path);
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw WindowsProjectIdentity.Error("Busy", "Another operation owns this project transaction", path, error.Message); }
    }
    private static void AssertEqual(SourceTreeSnapshot expected, SourceTreeSnapshot actual, string message, string id = "ConcurrentChange")
    { if (!StringComparer.Ordinal.Equals(SourceFingerprint.All(expected), SourceFingerprint.All(actual))) throw WindowsProjectIdentity.Error(id, message, actual.Root); }
    private static DomainException Resource(string name) => new("Transaction.ResourceLimit", "Project transaction exceeds its resource limit", "use explicit bounded quotas", "LimitsExceeded", name);
    private static string FailureSummary(Exception error)
    {
        var id = error is DomainException domain ? domain.Id : error.GetType().Name;
        return id + ": " + error.Message[..Math.Min(error.Message.Length, 2048)];
    }

    // Recovery and lifecycle helpers are kept in the same operation boundary.
}
