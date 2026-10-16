using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public sealed record RemovalOperationRequest(RemovalPlanRequest Plan, bool DryRun = false);
public sealed record RemovalOperationLimits(RemovalPlanningLimits? Planning = null, ProjectHealthLimits? Health = null,
    TransactionLimits? Transaction = null, NativeToolLimits? Tool = null, long MaximumArtifactBytes = 1L << 40);

public static class RemovalOperation
{
    public static Task<TransactionResult<RemovalPlan>> ExecuteAsync(ProjectContext project, RemovalOperationRequest request,
        string configurationDirectory, string stateDirectory, DateTimeOffset now, Func<PackwizObservation> resolvePackwiz, string nativeHost,
        RemovalOperationLimits? limits = null, TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Func<TransactionPreview<RemovalPlan>, CancellationToken, ValueTask>? prepareOutput = null,
        Func<TransactionEvent, ValueTask>? observer = null,
        Uri? providerBaseUri = null, Func<HttpMessageHandler>? providerHandler = null, Func<HttpMessageHandler>? artifactHandler = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Plan); ArgumentNullException.ThrowIfNull(resolvePackwiz); limits ??= new();
        if (limits.MaximumArtifactBytes <= 0) throw new DomainException("Remove.InvalidMaterializationPolicy", "Removal artifact quota must be positive", "use a bounded positive artifact quota");
        if (request.Plan.Selectors.IsDefaultOrEmpty || request.Plan.Selectors.Length > 1024 || request.Plan.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > 4096))
            throw new DomainException("Remove.InvalidRequest", "Removal requires bounded nonempty selectors", "select physical installed content");
        var config = MutationConfiguration.Resolve(project, configurationDirectory);
        var type = InventorySelector.NormalizeType(request.Plan.Type);
        var kinds = type switch { InventoryType.Mod => new[] { "mod" }, InventoryType.ResourcePack => ["resourcepack"], InventoryType.ShaderPack => ["shaderpack"], _ => ["mod", "resourcepack", "shaderpack"] };
        var selectors = BatchSelectorResolver.ResolveFromDirectory(request.Plan.Selectors, config, project.Id, now, new(kinds));
        var planning = request.Plan with { Selectors = selectors };
        return ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: request.DryRun, expected: expected,
            limits: limits.Transaction, cancellation: cancellation, prepareOutput: prepareOutput, observer: observer);

        async Task<RemovalPlan> Prepare(ProjectContext stage, CancellationToken token)
        {
            var before = await ProjectHealth.ReadAsync(stage, config, now, check: true, limits: limits.Health, cancellation: token,
                providerHandler: providerHandler?.Invoke(), artifactHandler: artifactHandler?.Invoke(), providerBaseUri: providerBaseUri).ConfigureAwait(false);
            var environment = new GraphEnvironment(stage.Minecraft ?? "", stage.Loader ?? "", stage.LoaderVersion ?? "", stage.JavaDeclared, stage.JavaVersion);
            var plan = RemovalPlanning.Prepare(environment, before.Nodes, planning, before.RuntimeOverrides, limits.Planning, token);
            RemovalMaterialization.Apply(stage, plan, limits.MaximumArtifactBytes, token);
            if (!plan.Changes.IsEmpty) _ = await PackwizRefresh.RefreshAsync(stage, resolvePackwiz(), nativeHost, limits.Tool, token).ConfigureAwait(false);
            var after = await ProjectHealth.ReadAsync(stage, config, now, limits: limits.Health, cancellation: token,
                providerHandler: providerHandler?.Invoke(), artifactHandler: artifactHandler?.Invoke(), providerBaseUri: providerBaseUri).ConfigureAwait(false);
            var retained = after.Nodes.ToDictionary(node => node.Graph.Id, StringComparer.OrdinalIgnoreCase);
            if (retained.Count != plan.Nodes.Length || plan.Nodes.Any(node => !retained.TryGetValue(node.Graph.Id, out var actual) || actual.Graph.VersionId != node.Graph.VersionId))
                throw new DomainException("Content.PlanMismatch", "Materialized removal differs from the plan or changed a retained version", "inspect the project metadata", "InvalidResult");
            GraphPolicy.Assert(after.RawReport, plan.Baseline, planning.Strict);
            ProjectContentNode Rebase(ProjectContentNode node) => node.Item.MetadataPath is { } path
                ? node with { Item = node.Item with { MetadataPath = TransactionFiles.Within(project.Root, Path.GetRelativePath(stage.Root, path)) } } : node;
            return plan with { Nodes = [.. plan.Nodes.Select(Rebase)], Changes = [.. plan.Changes.Select(change => change with { Before = Rebase(change.Before) })], Report = after.RawReport };
        }
    }
}
