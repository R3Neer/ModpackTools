using System.Collections.Immutable;
using ModpackTools.Core.Data;
using ModpackTools.Core.Dependencies;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Diagnostics;

public enum DoctorRepairScope { All, Environment, Project }
public sealed record DoctorRepairRequest(bool Apply = false, bool DryRun = false, bool Strict = false, bool AllowDowngrade = false, DoctorRepairScope Scope = DoctorRepairScope.All);
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
        Func<DoctorRepairResult, CancellationToken, ValueTask>? prepareOutput = null,
        Func<DoctorRepairResult, PackwizObservation, CancellationToken, ValueTask>? reviewProject = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Scope)) throw new DomainException("Doctor.InvalidScope", "Doctor repair scope is invalid", "use a supported doctor repair phase");
        if (request.Scope == DoctorRepairScope.Project && project is null)
            throw new DomainException("Project.NotSelected", "Project repair requires a selected project", "modpack project use <id>", "ObjectNotFound");
        PackwizObservation? observedTool = null;
        var installed = false; var configured = false; string? installedPath = null; TransactionResult<ContentPlan>? repaired = null;
        var diagnostics = ImmutableArray.CreateBuilder<DoctorRepairDiagnostic>();
        try
        {
            // Parse configuration before acquiring a dependency. An invalid user
            // file must not be replaced merely to make repair possible.
            var tool = Resolve();
            if (request.Scope != DoctorRepairScope.Project && !tool.Available && request.Apply && !request.DryRun)
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
                observedTool = PackwizRefresh.Resolve(acquired.Path, null, null, DependencyPackage.Packwiz.Version);
            }
            if (request.Scope != DoctorRepairScope.Environment && project is not null)
            {
                var plan = new ContentOperationRequest(new(ContentPlanOperation.Repair, [], Strict: request.Strict,
                    AllowDowngrade: request.AllowDowngrade), DryRun: true);
                repaired = await ContentOperation.ExecuteAsync(project, plan, configurationDirectory, stateDirectory,
                    now, Resolve, nativeHost, cancellation: cancellation).ConfigureAwait(false);
                if (reviewProject is not null) await reviewProject(new(installed, configured, installedPath, repaired, diagnostics.ToImmutable()), Resolve(), cancellation).ConfigureAwait(false);
                if (request.Apply && !request.DryRun && !repaired.Changes.IsEmpty)
                {
                    repaired = await ContentOperation.ExecuteAsync(project, plan with { DryRun = false }, configurationDirectory,
                        stateDirectory, now, Resolve, nativeHost, expected: repaired.Expectation, cancellation: cancellation,
                        prepareOutput: async (preview, token) =>
                        {
                            var receipt = new DoctorRepairResult(installed, configured, installedPath,
                                new(preview.Result, preview.Changes, !preview.Changes.IsEmpty, preview.Expectation, []), diagnostics.ToImmutable());
                            if (reviewProject is not null) await reviewProject(receipt, Resolve(), token).ConfigureAwait(false);
                            if (prepareOutput is not null) await prepareOutput(receipt, token).ConfigureAwait(false);
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

        PackwizObservation Resolve() => observedTool ??= PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory),
            Environment.GetEnvironmentVariable("PATH"), Path.Combine(configurationDirectory, "tools", "packwiz",
                DependencyPackage.Packwiz.Version, "packwiz.exe"), DependencyPackage.Packwiz.Version);
    }
}
