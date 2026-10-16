using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Source;
using ModpackTools.Core.Transactions;

internal static class ProjectTransactionContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools native transactions " + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "pack"); var state = Path.Combine(directory, "state"); Directory.CreateDirectory(root);
        var project = new ProjectContext("fixture", root, "Override name", "Override version", "fixture.mrpack", "1.21", "fabric", "0.16", "index.toml", Path.Combine(root, "index.toml"));
        try
        {
            File.WriteAllText(Path.Combine(root, "edit.txt"), "original"); File.WriteAllText(Path.Combine(root, "remove.txt"), "removed");
            File.WriteAllText(Path.Combine(root, "edit.txt") + ":fixture-data", "owned alternate stream");
            var original = SourceTree.Read(root); var phases = new List<TransactionPhase>();
            var preview = await ProjectTransaction.ExecuteAsync(project, state, Prepare, dryRun: true, observer: Observe);
            Assert(!preview.Applied && preview.Changes.Length == 3 && Same(original), "dry-run source unchanged and full write set");
            var applied = await ProjectTransaction.ExecuteAsync(project, state, Prepare, expected: preview.Expectation, observer: Observe);
            Assert(applied.Applied && File.ReadAllText(Path.Combine(root, "edit.txt")) == "updated" && !File.Exists(Path.Combine(root, "remove.txt")), "complete add/edit/delete");
            Assert(phases.IndexOf(TransactionPhase.JournalWritten) < phases.IndexOf(TransactionPhase.FileCommitted), "journal before first publication");
            Assert(applied.Result == "owned result" && applied.Diagnostics.IsEmpty, "owned callback result and cleanup");
            Assert(File.ReadAllText(Path.Combine(root, "edit.txt") + ":fixture-data") == "owned alternate stream", "alternate stream preserved across staging and commit");
            var noOp = await ProjectTransaction.ExecuteAsync(project, state, (stage, token) => Task.FromResult(stage.Name));
            Assert(!noOp.Applied && noOp.Result == project.Name, "context overrides preserved without descriptor reread");
            var stable = SourceTree.Read(root);
            try
            {
                _ = await ProjectTransaction.ExecuteAsync<string>(project, state, async (stage, token) => { await Prepare(stage, token); throw new IOException("prepare failure"); });
                throw new Exception("Prepare failure accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.IoFailed") { }
            Assert(Same(stable), "prepare failure preserves source");
            var faulted = false;
            try
            {
                _ = await ProjectTransaction.ExecuteAsync(project, state, (stage, token) =>
                {
                    File.WriteAllText(Path.Combine(stage.Root, "edit.txt"), "second update"); File.WriteAllText(Path.Combine(stage.Root, "other.txt"), "added");
                    return Task.FromResult(42);
                }, observer: value =>
                {
                    if (value.Phase == TransactionPhase.FileCommitted && !faulted) { faulted = true; throw new IOException("commit failure"); }
                    return ValueTask.CompletedTask;
                });
                throw new Exception("Commit failure accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.IoFailed") { }
            Assert(faulted && Same(stable), "partial commit rollback restores complete source");
            Assert(File.ReadAllText(Path.Combine(root, "edit.txt") + ":fixture-data") == "owned alternate stream", "alternate stream preserved across rollback");
            await ProjectTransaction.ExecuteAsync(project, state, (stage, token) => Task.FromResult(0), observer: async value =>
            {
                if (value.Phase != TransactionPhase.LeaseAcquired) return;
                try { _ = await ProjectTransaction.ExecuteAsync(project, Path.Combine(directory, "other state"), (stage, token) => Task.FromResult(0)); throw new Exception("Physical lease did not exclude another state namespace."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Transaction.Busy") { }
            });
            try
            {
                _ = await ProjectTransaction.ExecuteAsync(project, Path.Combine(root, "state"), (stage, token) => Task.FromResult(0));
                throw new Exception("State beneath project accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.UnsafeStateDirectory") { }
            Assert(!Directory.Exists(Path.Combine(root, "state")), "unsafe state rejected before creation");
            var late = await ProjectTransaction.ExecuteAsync(project, state, (stage, token) =>
            {
                File.WriteAllText(Path.Combine(stage.Root, "edit.txt"), "committed late"); return Task.FromResult(1);
            }, observer: value => value.Phase == TransactionPhase.Committed ? throw new OperationCanceledException("late notification") : ValueTask.CompletedTask);
            Assert(late.Applied && late.Diagnostics.Any(value => value.Code == "Transaction.CommittedNotificationFailed") && File.ReadAllText(Path.Combine(root, "edit.txt")) == "committed late", "post-marker cancellation remains applied");
            var pendingBefore = SourceTree.Read(root); string? workspace = null;
            try
            {
                _ = await ProjectTransaction.ExecuteAsync(project, state, (stage, token) =>
                {
                    File.WriteAllText(Path.Combine(stage.Root, "edit.txt"), "prepared pending"); return Task.FromResult(0);
                }, observer: value =>
                {
                    if (value.Phase == TransactionPhase.FileCommitted)
                    {
                        workspace = value.Workspace; File.WriteAllText(Path.Combine(root, "edit.txt"), "external edit"); throw new IOException("primary failure");
                    }
                    return ValueTask.CompletedTask;
                });
                throw new Exception("Recovery conflict accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.RecoveryFailed")
            { Assert(error.Details?.Contains("primary failure", StringComparison.Ordinal) == true && error.Details.Contains("RecoveryConflict", StringComparison.Ordinal), "both failures retained"); }
            Assert(workspace is not null && File.Exists(Path.Combine(workspace, "journal.json")) && File.ReadAllText(Path.Combine(root, "edit.txt")) == "external edit", "failed recovery preserves external data and evidence");
            File.WriteAllText(Path.Combine(root, "edit.txt"), "prepared pending");
            var recovered = await ProjectTransaction.ExecuteAsync(project, state, (stage, token) => Task.FromResult(0), dryRun: true);
            Assert(!recovered.Applied && Same(pendingBefore) && !Directory.Exists(workspace), "dry-run recovers pending before new preparation");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { _ = await ProjectTransaction.ExecuteAsync(project, state, (stage, token) => Task.FromResult(0), cancellation: cancelled.Token); throw new Exception("Cancelled transaction accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Core.OperationCancelled") { }

            Task<string> Prepare(ProjectContext stage, CancellationToken token)
            {
                Assert(stage.Name == project.Name && stage.Minecraft == project.Minecraft, "callback context values retained");
                Assert(File.ReadAllText(Path.Combine(stage.Root, "edit.txt") + ":fixture-data") == "owned alternate stream", "alternate stream present in stage before preparation");
                // Preserve the existing file's streams while changing its primary data.
                // WriteAllText recreates the file on Windows and drops its other streams.
                using (var output = new FileStream(Path.Combine(stage.Root, "edit.txt"), FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes("updated");
                    output.Write(bytes); output.SetLength(bytes.Length);
                }
                File.Delete(Path.Combine(stage.Root, "remove.txt"));
                Directory.CreateDirectory(Path.Combine(stage.Root, "new", "sub")); File.WriteAllText(Path.Combine(stage.Root, "new", "sub", "added.txt"), "added");
                return Task.FromResult("owned result");
            }
            ValueTask Observe(TransactionEvent value) { phases.Add(value.Phase); return ValueTask.CompletedTask; }
            bool Same(SourceTreeSnapshot expected) => SourceFingerprint.All(expected) == SourceFingerprint.All(SourceTree.Read(root));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static void Assert(bool value, string label) { if (!value) throw new Exception("Native transaction contract: " + label); }
}
