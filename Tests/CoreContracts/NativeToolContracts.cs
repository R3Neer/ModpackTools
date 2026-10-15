using System.Diagnostics;
using ModpackTools.Core;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Transactions;

internal static class NativeToolContracts
{
    public static async Task VerifyAsync(bool nativeProcessCases = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools owned native processes " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var missing = PackwizRefresh.Resolve(Path.Combine(directory, "missing.exe"), Environment.GetEnvironmentVariable("PATH"), Environment.ProcessPath);
            Assert(!missing.Available && missing.Source == "configured", "missing configured tool does not fall back");
            var root = Path.Combine(directory, "pack"); Directory.CreateDirectory(root);
            var project = ContentMaterializationInspection.Context(root); var state = Path.Combine(directory, "state"); string? retained = null;
            try
            {
                _ = await ProjectTransaction.ExecuteAsync<int>(project, state, (_, _) =>
                {
                    var error = new DomainException("Build.ProcessOwnershipUncertain", "synthetic uncertain owner", "inspect stage");
                    error.Data["ModpackTools.RetainStage"] = true; throw error;
                }, observer: value => { if (value.Phase == TransactionPhase.StageCopied) retained = value.Workspace; return ValueTask.CompletedTask; });
                throw new Exception("Uncertain owner accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Build.ProcessOwnershipUncertain") { }
            Assert(retained is not null && Directory.Exists(Path.Combine(retained, "stage")), "uncertain execution retains stage");
            try { _ = await ProjectTransaction.ExecuteAsync(project, state, (_, _) => Task.FromResult(0)); throw new Exception("Retained owner was cleaned."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.ActiveToolRetained") { }
            // Simulate failure to persist the terminal retention marker. The
            // existing preparing marker must fail closed on the NEXT invocation.
            var failedRoot = Path.Combine(directory, "marker failure pack"); Directory.CreateDirectory(failedRoot);
            var failedProject = ContentMaterializationInspection.Context(failedRoot); string? failedWorkspace = null;
            try
            {
                _ = await ProjectTransaction.ExecuteAsync<int>(failedProject, state, (_, _) =>
                {
                    var owner = Path.Combine(failedWorkspace!, "owner.json"); File.SetAttributes(owner, File.GetAttributes(owner) | FileAttributes.ReadOnly);
                    var error = new DomainException("Build.ProcessOwnershipUncertain", "original uncertain execution", "retain stage"); error.Data["ModpackTools.RetainStage"] = true; throw error;
                }, observer: value => { if (value.Phase == TransactionPhase.StageCopied) failedWorkspace = value.Workspace; return ValueTask.CompletedTask; });
                throw new Exception("Retention marker failure accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.StageRetentionMarkerFailed")
            { Assert(error.Details?.Contains("original uncertain execution", StringComparison.Ordinal) == true && error.Details.Contains("Retention marker:", StringComparison.Ordinal), "retention marker failure keeps both causes"); }
            finally
            {
                if (failedWorkspace is not null)
                {
                    var owner = Path.Combine(failedWorkspace, "owner.json"); File.SetAttributes(owner, File.GetAttributes(owner) & ~FileAttributes.ReadOnly);
                }
            }
            try { _ = await ProjectTransaction.ExecuteAsync(failedProject, state, (_, _) => Task.FromResult(0)); throw new Exception("Unconfirmed preparation was deleted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.PreparationInterrupted") { }
            Assert(Directory.Exists(Path.Combine(failedWorkspace!, "stage")), "failed retention marker survives restart");
            // The remaining checks require the frozen Native host's private child
            // entry point; managed build execution is supplementary only.
            if (!nativeProcessCases) return;
            var executable = Environment.ProcessPath!;
            var result = await NativeTool.RunAsync(executable, ["--tool-fixture", "both-ok"], root, executable);
            Assert(result.ExitCode == 0 && result.Stdout == "fixture stdout\r\n" && result.Stderr == "fixture stderr\r\n", "separate stdout/stderr capture");
            await Fails("both-fail", "ModpackTools.Build.ProcessFailed");
            await Fails("flood", "ModpackTools.Build.ProcessOutputLimit", new(MaximumOutputBytes: 16));
            await Fails("spawn-and-hang", "ModpackTools.Build.ProcessTimeout", new(TimeoutMilliseconds: 1000));
            AssertStopped();
            File.Delete(Path.Combine(root, "fixture-writer.pid"));
            using var cancel = new CancellationTokenSource(1000);
            await Fails("spawn-and-hang", "ModpackTools.Core.OperationCancelled", cancellation: cancel.Token);
            AssertStopped();
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"fixture\"\n[index]\nfile=\"missing.toml\"\n");
            try { _ = await PackwizRefresh.RefreshAsync(project, new(true, executable, "fixture", null), executable); throw new Exception("Exit-zero invalid refresh accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Build.InvalidRefresh") { }

            async Task Fails(string mode, string id, NativeToolLimits? limits = null, CancellationToken cancellation = default)
            {
                try { _ = await NativeTool.RunAsync(executable, ["--tool-fixture", mode], root, executable, limits, cancellation); throw new Exception("Tool failure accepted: " + mode); }
                catch (DomainException error) when (error.Id == id) { }
            }
            void AssertStopped()
            {
                foreach (var name in new[] { "fixture-parent.pid", "fixture-writer.pid" })
                {
                    var path = Path.Combine(root, name); Assert(File.Exists(path), "owned fixture actually started");
                    var pid = int.Parse(File.ReadAllText(path), System.Globalization.CultureInfo.InvariantCulture);
                    try { using var process = Process.GetProcessById(pid); Assert(process.HasExited, "owned fixture process exited"); }
                    catch (ArgumentException) { }
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool value, string label) { if (!value) throw new Exception("Native tool contract: " + label); }
}
