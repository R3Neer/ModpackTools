using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core.Source;

namespace ModpackTools.Core.Transactions;

public static partial class FileTreeTransaction
{
    private static ImmutableArray<string> CreatedDirectories(string root, ImmutableArray<SourceChange> changes, SourceTreeReadLimits limits)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long characters = 0;
        foreach (var change in changes)
        {
            var destination = TransactionFiles.Within(root, change.Path);
            if (Directory.Exists(destination)) throw WindowsProjectIdentity.Error("UnsupportedTopology", "File/directory replacement is not supported by this transaction", change.Path);
            var parent = Path.GetDirectoryName(destination)!;
            while (!Directory.Exists(parent))
            {
                if (File.Exists(parent)) throw WindowsProjectIdentity.Error("UnsupportedTopology", "A new file parent is occupied by an existing file", parent);
                var relative = Path.GetRelativePath(root, parent).Replace('\\', '/'); _ = TransactionFiles.Within(root, relative);
                if (paths.Add(relative))
                {
                    characters += relative.Length;
                    if (paths.Count > limits.MaximumDirectories || characters > limits.MaximumPathCharacters) throw Resource("CreatedDirectories");
                }
                parent = Path.GetDirectoryName(parent)!;
            }
        }
        return [.. paths.OrderByDescending(path => path.Length).ThenBy(path => path, StringComparer.Ordinal)];
    }

    private static void ValidateBackups(string workspace, ImmutableArray<SourceChange> changes, long maximumBytes)
    {
        foreach (var change in changes)
        {
            if (change.Before is null) continue;
            var backup = TransactionFiles.Within(Path.Combine(workspace, "backup"), change.Path);
            if (TransactionFiles.Hash(backup, maximumBytes) != change.Before)
                throw WindowsProjectIdentity.Error("InvalidBackup", "Transaction backup is damaged or missing", backup);
        }
    }

    private static void RecoverAll(string home, WindowsProjectIdentity identity, TransactionLimits limits, ImmutableArray<TransactionDiagnostic>.Builder diagnostics)
    {
        var owned = new List<(string Workspace, TransactionOwner Owner, TransactionJournal? Journal)>(); var count = 0;
        var directories = new List<DirectoryInfo>();
        foreach (var directory in new DirectoryInfo(home).EnumerateDirectories())
        {
            if (directories.Count >= limits.MaximumWorkspaces) throw Resource(nameof(limits.MaximumWorkspaces));
            directories.Add(directory);
        }
        foreach (var directory in directories.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            if (++count > limits.MaximumWorkspaces) throw Resource(nameof(limits.MaximumWorkspaces));
            WindowsProjectIdentity.GuardAncestors(directory.FullName);
            if (!Guid.TryParseExact(directory.Name, "N", out _) || !File.Exists(Path.Combine(directory.FullName, "owner.json")))
            {
                diagnostics.Add(new("Transaction.UnownedWorkspace", directory.FullName, "Directory has no recognized ownership marker and was preserved")); continue;
            }
            var owner = ReadOwned(directory.FullName, identity.Value, limits);
            if (owner.Status == "retained") throw WindowsProjectIdentity.Error("ActiveToolRetained", "A previous tool could not be confirmed stopped; its stage was retained", directory.FullName);
            var journalPath = Path.Combine(directory.FullName, "journal.json");
            TransactionJournal? journal = null;
            if (File.Exists(journalPath))
            {
                try { journal = TransactionJournalStore.ReadJournal(directory.FullName, owner, limits); }
                catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException)
                { throw TransactionJournalStore.Invalid(journalPath, error.Message); }
            }
            else if (owner.Status == "pending") throw WindowsProjectIdentity.Error("MissingJournal", "Pending transaction journal is missing; workspace was preserved", directory.FullName);
            else if (owner.Status == "preparing") throw WindowsProjectIdentity.Error("PreparationInterrupted", "Previous preparation has no confirmed safe close; its stage was preserved", directory.FullName);
            owned.Add((directory.FullName, owner, journal));
        }
        // Parse and bind every owned journal before restoring or deleting a workspace.
        foreach (var item in owned)
        {
            identity.AssertCurrent();
            if (item.Journal is { Status: "pending" } journal)
                Restore(item.Workspace, journal, identity, (limits.RecoveryTree ?? new()).MaximumBytes, limits);
            else if (item.Journal is { Status: "committed" })
                TransactionJournalStore.WriteOwner(item.Workspace, item.Owner with { Status = "committed" }, limits);
            CleanupCompleted(home, item.Workspace, identity.Value, limits, diagnostics, permitCommittedJournal: true);
        }
    }

    private static void Restore(string workspace, TransactionJournal journal, WindowsProjectIdentity identity, long maximumBytes, TransactionLimits limits)
    {
        identity.AssertCurrent(); var root = identity.Value.CanonicalRoot;
        // Validate ALL destinations and ALL backups before the first restoration.
        foreach (var change in journal.Changes)
        {
            var target = TransactionFiles.Within(root, change.Path); var current = TransactionFiles.Hash(target, maximumBytes);
            if (current != change.Before && current != change.After)
                throw WindowsProjectIdentity.Error("RecoveryConflict", "Recovery would overwrite an external edit", change.Path, Path.Combine(workspace, "journal.json"));
        }
        ValidateBackups(workspace, journal.Changes, maximumBytes);
        foreach (var relative in journal.PublicationTemporaries)
        {
            var temporary = TransactionFiles.Within(root, relative);
            if (Directory.Exists(temporary)) throw WindowsProjectIdentity.Error("RecoveryConflict", "Reserved publication temporary became a directory", relative);
            if (File.Exists(temporary)) TransactionFiles.DeleteOwnedFile(temporary);
        }
        for (var index = 0; index < journal.Changes.Length; index++)
        {
            var change = journal.Changes[index];
            identity.AssertCurrent(); var target = TransactionFiles.Within(root, change.Path);
            // Recheck at the write boundary in addition to the all-file pass.
            var current = TransactionFiles.Hash(target, maximumBytes);
            if (current != change.Before && current != change.After) throw WindowsProjectIdentity.Error("RecoveryConflict", "A destination changed during recovery", change.Path);
            if (current == change.Before) continue;
            if (change.Before is not null)
                TransactionFiles.CopyVerified(TransactionFiles.Within(Path.Combine(workspace, "backup"), change.Path), target, change.Before, maximumBytes, CancellationToken.None,
                    TransactionFiles.Within(root, TransactionFiles.PublicationRelative(change.Path, journal.Owner.TransactionId, index, true)));
            else if (File.Exists(target)) TransactionFiles.DeleteOwnedFile(target);
        }
        foreach (var relative in journal.CreatedDirectories.OrderByDescending(path => path.Length).ThenBy(path => path, StringComparer.Ordinal))
        {
            var path = TransactionFiles.Within(root, relative);
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
        foreach (var change in journal.Changes)
            if (TransactionFiles.Hash(TransactionFiles.Within(root, change.Path), maximumBytes) != change.Before)
                throw WindowsProjectIdentity.Error("RecoveryVerificationFailed", "Restored file differs from its backup state", change.Path);
        TransactionJournalStore.WriteOwner(workspace, journal.Owner with { Status = "rolledback" }, limits);
        File.Delete(Path.Combine(workspace, "journal.json"));
    }

    private static TransactionOwner ReadOwned(string workspace, ProjectPhysicalIdentity project, TransactionLimits limits)
    {
        try { return TransactionJournalStore.ReadOwner(workspace, project, limits); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException)
        { throw TransactionJournalStore.Invalid(workspace, error.Message); }
    }

    private static void CleanupCompleted(string home, string workspace, ProjectPhysicalIdentity project, TransactionLimits limits,
        ImmutableArray<TransactionDiagnostic>.Builder diagnostics, bool permitCommittedJournal = false)
    {
        if (!Directory.Exists(workspace)) return;
        try
        {
            var owner = ReadOwned(workspace, project, limits); var journalPath = Path.Combine(workspace, "journal.json");
            if (owner.Status == "retained") return;
            if (File.Exists(journalPath))
            {
                if (!permitCommittedJournal || TransactionJournalStore.ReadJournal(workspace, owner, limits).Status != "committed") return;
            }
            else if (owner.Status == "pending") return;
            TransactionFiles.DeleteOwnedTree(home, workspace, limits.MaximumCleanupEntries);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DomainException or JsonException or InvalidOperationException)
        { diagnostics.Add(new("Transaction.WorkspaceCleanupPending", workspace, error.Message)); }
    }
}
