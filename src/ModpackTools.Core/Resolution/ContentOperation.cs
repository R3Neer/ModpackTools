using System.Collections.Immutable;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public sealed record ContentOperationRequest(ContentPlanRequest Plan, bool DryRun = false, string? Category = null, int? EnableAt = null);
public sealed record ContentOperationLimits(ContentPlanLimits? Plan = null, TransactionLimits? Transaction = null, NativeToolLimits? Tool = null);

public static class ContentOperation
{
    public static async Task<TransactionResult<ContentPlan>> ExecuteAsync(ProjectContext project, ContentOperationRequest request,
        string configurationDirectory, string stateDirectory, DateTimeOffset now, Func<PackwizObservation> resolvePackwiz, string nativeHost,
        ContentOperationLimits? limits = null, TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Uri? providerBaseUri = null, Func<HttpMessageHandler>? providerHandler = null, Func<HttpMessageHandler>? artifactHandler = null,
        Func<TransactionPreview<ContentPlan>, CancellationToken, ValueTask>? prepareOutput = null,
        Func<TransactionEvent, ValueTask>? observer = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(resolvePackwiz); limits ??= new();
        var policy = limits.Plan ?? new(); var planning = request.Plan;
        if (policy.MaximumSelectors <= 0 || policy.MaximumSelectorCharacters <= 0 || policy.MaximumDetailCharacters <= 0)
            throw new DomainException("Compatibility.InvalidPlanPolicy", "Content-plan quotas must be positive", "use positive bounded plan quotas");
        (policy.Choices ?? new()).Validate(); DependencyResolver.ValidatePolicy(policy.Search ?? new());
        if (!Enum.IsDefined(planning.Operation) || planning.Selectors.IsDefault || planning.Selectors.Length > policy.MaximumSelectors
            || planning.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > policy.MaximumSelectorCharacters)
            || planning.To?.Length > policy.MaximumSelectorCharacters || planning.Operation == ContentPlanOperation.Update && !planning.All && planning.Selectors.IsEmpty)
            throw new DomainException("Content.InvalidPlanRequest", "Content operation request is invalid", "use bounded selectors");
        var config = Path.GetFullPath(configurationDirectory);
        if (StringComparer.OrdinalIgnoreCase.Equals(config, Path.GetFullPath(project.Root)) || TransactionFiles.IsBelow(config, project.Root))
            throw new DomainException("Transaction.UnsafeConfigurationDirectory", "Content configuration/cache must be outside the protected project", "choose a separate configuration directory", "InvalidOperation", config);
        var selectors = planning.Selectors;
        if (planning.Operation == ContentPlanOperation.Add)
        {
            var choices = new SavedProviderChoices(config, now, policy.Choices);
            selectors = [.. selectors.Select(selector => choices.ResolveSearchNumber(selector, project) is { } id ? "modrinth:" + id : selector)];
        }
        else if (planning.Operation == ContentPlanOperation.Update)
        {
            var type = InventorySelector.NormalizeType(planning.Type);
            var kinds = type switch { InventoryType.Mod => new[] { "mod" }, InventoryType.ResourcePack => ["resourcepack"], InventoryType.ShaderPack => ["shaderpack"], _ => ["mod", "resourcepack", "shaderpack"] };
            selectors = BatchSelectorResolver.ResolveFromDirectory(selectors, config, project.Id, now, new(kinds, requirePackwiz: true), policy.Choices);
        }
        planning = planning with { Selectors = selectors };
        PackwizObservation? resolvedTool = null;
        PackwizObservation Tool() => resolvedTool ??= resolvePackwiz();
        return await ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: request.DryRun,
            expected: expected, limits: limits.Transaction, cancellation: cancellation, prepareOutput: prepareOutput, observer: observer).ConfigureAwait(false);

        async Task<ContentPlan> Prepare(ProjectContext stage, CancellationToken token)
        {
            ResolutionAcquisition? source = null;
            try
            {
                source = await Acquire().ConfigureAwait(false); var initial = source.InitialObservation!; var baseline = initial.RawReport;
                ImmutableArray<InventoryItem> targets = [];
                if (planning.Operation == ContentPlanOperation.Update)
                {
                    targets = planning.All ? AllTargets(initial) : UpdateTargetSelector.Resolve(initial.Inventory, selectors, planning.Type);
                    var before = source.Before.ToDictionary(node => node.Graph.Id, StringComparer.OrdinalIgnoreCase); var providerChanged = false;
                    foreach (var target in targets)
                    {
                        if (before[target.Id].Pinned) throw new DomainException("Compatibility.Pinned", $"'{target.Name}' is pinned", "modpack content unpin " + target.Id, "InvalidOperation");
                        if (target.Id.StartsWith("modrinth:", StringComparison.Ordinal)) continue;
                        if (!string.IsNullOrEmpty(planning.To)) throw new DomainException("Versions.UnsupportedProvider", "Exact version selection requires Modrinth", "omit --to for this provider");
                        var stem = PhysicalAliasIndex.MetadataStem(target.MetadataPath!);
                        var tool = Tool(); if (!tool.Available || tool.Path is null) throw MissingTool(tool);
                        _ = await NativeTool.RunAsync(tool.Path, ["update", stem, "--yes"], stage.Root, nativeHost, limits.Tool, token).ConfigureAwait(false);
                        providerChanged = true;
                    }
                    if (providerChanged) { await source.DisposeAsync().ConfigureAwait(false); source = null; source = await Acquire().ConfigureAwait(false); }
                }
                var plan = await ContentPlan.PrepareWithSourceAsync(stage, planning, config, now, source, policy, token).ConfigureAwait(false);
                var selected = plan.Nodes.ToDictionary(node => node.Graph.Id, StringComparer.OrdinalIgnoreCase); string? category = null;
                if (!string.IsNullOrEmpty(request.Category))
                {
                    category = CategorySelector.ResolveFromProject(stage.Root, stage.Id, config, request.Category, now, allowUnclassified: true, policy.Choices);
                    if (plan.Requested.Any(id => selected[id].Item.Kind != ContentKind.Mod))
                        throw new DomainException("Option.CategoryRequiresMod", "--category applies only to requested mods", "remove --category from mixed content batches");
                }
                if (request.EnableAt.HasValue && plan.Requested.Any(id => selected[id].Item.Kind != ContentKind.ResourcePack))
                    throw new DomainException("Option.EnableRequiresResource", "--enable-at requires a resource pack batch", "add mods and shaders separately");
                _ = ContentMaterialization.Apply(stage, stage, plan, cancellation: token);
                if (category is not null) RequestedContentOptions.Classify(stage, plan.Requested, category);
                if (request.EnableAt is { } position) RequestedContentOptions.Enable(stage, plan.Requested, position);
                var after = await ProjectHealth.ReadAsync(stage, config, now, limits: policy.Acquisition?.Observation, cancellation: token,
                    providerHandler: providerHandler?.Invoke(), artifactHandler: artifactHandler?.Invoke(), providerBaseUri: providerBaseUri).ConfigureAwait(false);
                var versions = after.Nodes.ToDictionary(node => node.Graph.Id, StringComparer.OrdinalIgnoreCase);
                foreach (var change in plan.Changes)
                    if (!versions.TryGetValue(change.Id, out var node) || node.Graph.VersionId != change.After.Graph.VersionId)
                        throw new DomainException("Content.PlanMismatch", "Materialized content differs from the frozen plan", "inspect the generated metadata", "InvalidResult");
                GraphPolicy.Assert(planning.Operation == ContentPlanOperation.Repair ? plan.Report : after.RawReport,
                    planning.Operation == ContentPlanOperation.Repair ? null : baseline, planning.Strict);
                if (category is not null || planning.Operation == ContentPlanOperation.Add && !plan.Requested.IsEmpty || !plan.Changes.IsEmpty
                    || request.EnableAt.HasValue || planning.Operation == ContentPlanOperation.Repair || !targets.IsEmpty)
                    _ = await PackwizRefresh.RefreshAsync(stage, Tool(), nativeHost, limits.Tool, token).ConfigureAwait(false);
                return Rebase(plan, stage, project);
            }
            finally { if (source is not null) await source.DisposeAsync().ConfigureAwait(false); }

            Task<ResolutionAcquisition> Acquire() => ResolutionAcquisition.CreateAsync(stage, config, now, policy.Acquisition, token,
                providerHandler?.Invoke(), artifactHandler?.Invoke(), providerBaseUri);
        }

        ImmutableArray<InventoryItem> AllTargets(ProjectHealthObservation observation)
        {
            var type = InventorySelector.NormalizeType(planning.Type); var pinned = observation.Nodes.Where(node => node.Pinned).Select(node => node.Graph.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return [.. observation.Inventory.Mods.Concat(observation.Inventory.Resources).Concat(observation.Inventory.Shaders)
                .Where(item => item.Source == ContentSource.Packwiz && !pinned.Contains(item.Id) && (type == InventoryType.All || type.ToString().Equals(item.Kind.ToString(), StringComparison.OrdinalIgnoreCase)))];
        }
    }
    private static ContentPlan Rebase(ContentPlan plan, ProjectContext stage, ProjectContext original)
    {
        ProjectContentNode Node(ProjectContentNode node) => node.Item.MetadataPath is { } path
            ? node with { Item = node.Item with { MetadataPath = TransactionFiles.Within(original.Root, Path.GetRelativePath(stage.Root, path)) } } : node;
        return plan with { Nodes = [.. plan.Nodes.Select(Node)], Changes = [.. plan.Changes.Select(change => change with { Before = change.Before is { } before ? Node(before) : null, After = Node(change.After) })] };
    }
    private static DomainException MissingTool(PackwizObservation tool) => new("Dependency.PackwizNotFound", "Required Packwiz executable is not available", "configure an existing native Packwiz executable", "ObjectNotFound", tool.Path);
}
