using System.Collections.Immutable;
using ModpackTools.Core.Data;
using ModpackTools.Core.Dependencies;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Diagnostics;

public sealed record DoctorRepairRequest(bool Apply = false, bool DryRun = false, bool Strict = false, bool AllowDowngrade = false);
public sealed record DoctorRepairDiagnostic(string Code, string Path, string Message, string? Details = null, bool RetainedEvidence = false);
public sealed record DoctorRepairResult(bool PackwizInstalled, bool ConfigurationUpdated, string? PackwizPath,
    TransactionResult<ContentPlan>? Project, ImmutableArray<DoctorRepairDiagnostic> Diagnostics)
{
    public bool Applied => PackwizInstalled || ConfigurationUpdated || Project?.Applied == true;
}

public static class DoctorRepair
{
    public static async Task<DoctorRepairResult> ExecuteAsync(string configurationDirectory, string stateDirectory,
        string temporaryParent, string nativeHost, ProjectContext? project, DoctorRepairRequest request,
        DateTimeOffset now, CancellationToken cancellation = default,
        Func<DoctorRepairResult, CancellationToken, ValueTask>? prepareOutput = null)
    {
        var installed = false; var configured = false; string? installedPath = null; TransactionResult<ContentPlan>? repaired = null;
        var diagnostics = ImmutableArray.CreateBuilder<DoctorRepairDiagnostic>();
        try
        {
            // Parse configuration before acquiring a dependency. An invalid user
            // file must not be replaced merely to make repair possible.
            var tool = Resolve();
            if (!tool.Available && request.Apply && !request.DryRun)
            {
                var futurePath = Path.Combine(configurationDirectory, "tools", "packwiz", DependencyPackage.Packwiz.Version, "packwiz.exe");
                if (prepareOutput is not null) await prepareOutput(new(true, true, futurePath, null, []), cancellation).ConfigureAwait(false);
                var acquired = await ManagedPackwiz.InstallAsync(configurationDirectory, stateDirectory, temporaryParent,
                    nativeHost, cancellation: cancellation).ConfigureAwait(false);
                installed = acquired.Installed; installedPath = acquired.Path;
                diagnostics.AddRange(acquired.Diagnostics.Select(value => new DoctorRepairDiagnostic(value.Code, value.Path, value.Message)));
                if (prepareOutput is not null) await prepareOutput(new(installed, true, installedPath, null, diagnostics.ToImmutable()), cancellation).ConfigureAwait(false);
                await ConfigurationStore.SetAsync(configurationDirectory, new(ConfigurationSetting.Packwiz, acquired.Path), cancellation).ConfigureAwait(false);
                configured = true;
            }
            if (project is not null)
            {
                var plan = new ContentOperationRequest(new(ContentPlanOperation.Repair, [], Strict: request.Strict,
                    AllowDowngrade: request.AllowDowngrade), DryRun: true);
                repaired = await ContentOperation.ExecuteAsync(project, plan, configurationDirectory, stateDirectory,
                    now, Resolve, nativeHost, cancellation: cancellation).ConfigureAwait(false);
                if (request.Apply && !request.DryRun && !repaired.Changes.IsEmpty)
                {
                    repaired = await ContentOperation.ExecuteAsync(project, plan with { DryRun = false }, configurationDirectory,
                        stateDirectory, now, Resolve, nativeHost, expected: repaired.Expectation, cancellation: cancellation,
                        prepareOutput: async (preview, token) =>
                        {
                            if (prepareOutput is not null) await prepareOutput(new(installed, configured, installedPath,
                                new(preview.Result, preview.Changes, !preview.Changes.IsEmpty, preview.Expectation, []), diagnostics.ToImmutable()), token).ConfigureAwait(false);
                        }).ConfigureAwait(false);
                }
                diagnostics.AddRange(repaired.Diagnostics.Select(value => new DoctorRepairDiagnostic(value.Code, value.Path, value.Message)));
            }
        }
        catch (Exception error) when ((installed || configured) && error is DomainException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Dependency publication is already real, even if config/project work
            // fails later. Keep that receipt and the exact causal failure.
            // A rejected uncommitted preview may itself exceed the host's output
            // quota. Keep only the bounded publication receipt in that case.
            if (repaired?.Applied != true) repaired = null;
            diagnostics.Add(new(error is DomainException domain ? domain.Id : "Doctor.RepairIncomplete",
                error is DomainException { Target: TextTarget target } ? target.Value : installedPath!, error.Message,
                (error as DomainException)?.Details, error.Data["ModpackTools.RetainStage"] is true));
        }
        return new(installed, configured, installedPath, repaired, diagnostics.ToImmutable());

        PackwizObservation Resolve() => PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory),
            Environment.GetEnvironmentVariable("PATH"), Path.Combine(configurationDirectory, "tools", "packwiz",
                DependencyPackage.Packwiz.Version, "packwiz.exe"), DependencyPackage.Packwiz.Version);
    }
}
