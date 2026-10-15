using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;

namespace ModpackTools.Core.Resolution;

public enum ContentPlanOperation { Add, Update, Repair }
public sealed record ContentPlanRequest(ContentPlanOperation Operation, ImmutableArray<string> Selectors,
    bool All = false, string Type = "all", string? To = null, bool Strict = false, bool AllowDowngrade = false);
public sealed record ContentPlanLimits(ResolutionAcquisitionLimits? Acquisition = null, ResolutionLimits? Search = null,
    ReferenceCacheReadLimits? Choices = null, int MaximumSelectors = 1024, int MaximumSelectorCharacters = 4096,
    int MaximumDetailCharacters = 1024 * 1024);
public sealed record PlannedContentChange(string Id, ProjectContentNode? Before, ProjectContentNode After, string Reason);

// An observation-based proposal, not an executable transaction. File identities,
// fingerprints, exclusive ownership and write/recovery belong to the write boundary.
public sealed partial record ContentPlan(ImmutableArray<ProjectContentNode> Nodes, ImmutableArray<PlannedContentChange> Changes,
    ImmutableArray<string> Requested, GraphReport Baseline, GraphReport Report, ContentPlanOperation Operation)
{
    [GeneratedRegex("""\Ahttps?://(?:www\.)?modrinth\.com/(?:mod|resourcepack|shader)/([^/]+)(?:/version/([^/]+))?/?\z""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ProjectUrlPattern();

    public static async Task<ContentPlan> PrepareAsync(ProjectContext project, ContentPlanRequest request,
        string configurationDirectory, DateTimeOffset now, ContentPlanLimits? limits = null, CancellationToken cancellation = default,
        HttpMessageHandler? providerHandler = null, HttpMessageHandler? artifactHandler = null, Uri? providerBaseUri = null)
    {
        try { return await PrepareCore(project, request, configurationDirectory, now, limits, cancellation, providerHandler, artifactHandler, providerBaseUri).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Content planning was cancelled", "retry when ready", "OperationStopped"); }
    }

    private static async Task<ContentPlan> PrepareCore(ProjectContext project, ContentPlanRequest request,
        string configurationDirectory, DateTimeOffset now, ContentPlanLimits? limits, CancellationToken cancellation,
        HttpMessageHandler? providerHandler, HttpMessageHandler? artifactHandler, Uri? providerBaseUri)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(request);
        limits ??= new();
        if (limits.MaximumSelectors <= 0 || limits.MaximumSelectorCharacters <= 0 || limits.MaximumDetailCharacters <= 0)
            throw new DomainException("Compatibility.InvalidPlanPolicy", "Content-plan quotas must be positive", "use positive bounded plan quotas");
        if (!Enum.IsDefined(request.Operation) || request.Selectors.IsDefault || request.Selectors.Length > limits.MaximumSelectors
            || request.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > limits.MaximumSelectorCharacters)
            || request.To?.Length > limits.MaximumSelectorCharacters || request.Operation == ContentPlanOperation.Update && !request.All && request.Selectors.IsEmpty)
            throw new DomainException("Content.InvalidPlanRequest", "Content-plan request is invalid or exceeds its selector quota", "use a bounded supported operation and selectors");
        (limits.Choices ?? new()).Validate(); DependencyResolver.ValidatePolicy(limits.Search ?? new());
        await using var source = await ResolutionAcquisition.CreateAsync(project, configurationDirectory, now,
            limits.Acquisition, cancellation, providerHandler, artifactHandler, providerBaseUri).ConfigureAwait(false);
        var observation = source.InitialObservation!; var baseline = observation.Report;
        var before = source.Before.ToDictionary(node => node.Graph.Id, StringComparer.OrdinalIgnoreCase);
        var nodes = before.ToDictionary(pair => pair.Key, pair => ResolutionAcquisition.ToResolutionNode(pair.Value), StringComparer.OrdinalIgnoreCase);
        var nodeOrder = source.Before.Select(node => node.Graph.Id).ToList();
        var roots = new List<string>(); var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var choices = new SavedProviderChoices(configurationDirectory, now, limits.Choices);
        if (request.Operation == ContentPlanOperation.Add)
        {
            foreach (var selector in request.Selectors)
            {
                cancellation.ThrowIfCancellationRequested();
                var identity = choices.ResolveSearchNumber(selector, project) ?? RemovePrefix(selector);
                var url = ProjectUrlPattern().Match(identity); string? urlVersion = null;
                if (url.Success) { identity = url.Groups[1].Value; urlVersion = url.Groups[2].Success ? url.Groups[2].Value : null; }
                var info = await source.LookupProjectAsync(identity, cancellation).ConfigureAwait(false);
                var id = "modrinth:" + info.Id;
                if (!requested.Add(id)) continue;
                var domain = await source.ObserveCandidatesAsync(id, cancellation).ConfigureAwait(false);
                if (domain.Versions.IsEmpty) throw NoCompatible(identity);
                var chosen = string.IsNullOrEmpty(urlVersion) ? domain.Versions[0] : domain.Versions.FirstOrDefault(version => Exact(version.Id, urlVersion) || Exact(version.VersionNumber, urlVersion));
                if (chosen is null) throw new DomainException("Versions.Incompatible", "The requested URL version is incompatible", "modpack content versions <selector>");
                if (before.TryGetValue(id, out var installed) && installed.Pinned && !Exact(installed.Graph.VersionId, chosen.Id)) throw Pinned(identity, id);
                var acquired = await source.GetNodeAsync(id, chosen.Id, cancellation).ConfigureAwait(false);
                if (!nodes.ContainsKey(id)) nodeOrder.Add(id);
                nodes[id] = acquired; roots.Add(id);
            }
        }
        else if (request.Operation == ContentPlanOperation.Update)
        {
            var targets = request.All ? AllTargets() : UpdateTargetSelector.Resolve(observation.Inventory, request.Selectors, request.Type);
            foreach (var target in targets)
            {
                cancellation.ThrowIfCancellationRequested();
                if (before[target.Id].Pinned) throw Pinned(target.Name, target.Id);
                if (!target.Id.StartsWith("modrinth:", StringComparison.Ordinal)) continue;
                var domain = await source.ObserveCandidatesAsync(target.Id, cancellation).ConfigureAwait(false);
                if (domain.Versions.IsEmpty) throw new DomainException("Versions.NoCompatibleVersion", $"No compatible version for '{target.Name}'", "check project compatibility", "ObjectNotFound");
                var chosen = domain.Versions[0];
                if (!string.IsNullOrEmpty(request.To))
                {
                    var numeric = ReferenceCacheResolver.IsNumericSelector(request.To);
                    var selected = numeric ? choices.ResolveVersionChoice(request.To, project, target) : request.To;
                    var matches = domain.Versions.Where(version => Exact(version.Id, selected) || !numeric && Exact(version.VersionNumber, selected)).ToArray();
                    if (matches.Length != 1) throw new DomainException("Versions.NotFound", $"Version '{request.To}' is unavailable, incompatible or ambiguous",
                        "refresh modpack content versions and select an exact version ID");
                    chosen = matches[0];
                }
                nodes[target.Id] = await source.GetNodeAsync(target.Id, chosen.Id, cancellation).ConfigureAwait(false);
                roots.Add(target.Id); requested.Add(target.Id);
            }
        }
        foreach (var id in roots)
            if (!request.AllowDowngrade && string.IsNullOrEmpty(request.To) && before.TryGetValue(id, out var old)
                && DependencyResolver.IsDowngrade(ResolutionAcquisition.ToResolutionNode(old), nodes[id]))
                throw new DomainException("Compatibility.DowngradeBlocked", $"The latest publication of '{id}' would downgrade its installed version",
                    "use --allow-downgrade or select a version explicitly with update --to", "InvalidOperation");
        var environment = new GraphEnvironment(project.Minecraft ?? "", project.Loader ?? "", project.LoaderVersion ?? "", project.JavaDeclared, project.JavaVersion);
        var search = await DependencyResolver.SolveAsync(new(environment, [.. source.Before.Select(ResolutionAcquisition.ToResolutionNode)],
            [.. nodeOrder.Select(id => nodes[id])], [.. roots], request.Operation == ContentPlanOperation.Repair ? [] : [.. baseline.Errors.Select(issue => issue.Key)],
            request.Strict, request.AllowDowngrade, observation.RuntimeOverrides, baseline), source, limits.Search, cancellation).ConfigureAwait(false);
        if (search.Solution is not { } solution)
        {
            var report = search.LastReport ?? baseline; var details = new StringBuilder();
            foreach (var message in report.Errors.Select(issue => issue.Message).Concat(report.Unknown.Select(issue => issue.Message))
                .Concat(source.Before.Where(node => node.Pinned).Select(node => node.Item.Name + " is pinned")))
            {
                if (message.Length + (details.Length == 0 ? 0L : 2L) > limits.MaximumDetailCharacters - details.Length)
                    throw new DomainException("Compatibility.PlanDetailLimit", "Dependency-plan failure details exceed their quota", "inspect a bounded health report", "LimitsExceeded");
                if (details.Length != 0) details.Append("; "); details.Append(message);
            }
            throw new DomainException("Compatibility.Unresolvable", "No verified dependency solution satisfies this request",
                "adjust the requested versions, unpin a dependency, or use --allow-downgrade when a downgrade is required", "InvalidOperation", details: details.ToString());
        }
        cancellation.ThrowIfCancellationRequested();
        var selectedNodes = solution.Nodes.Select(source.ExportSelectedNode).ToImmutableArray();
        var changes = ImmutableArray.CreateBuilder<PlannedContentChange>();
        foreach (var node in selectedNodes.OrderBy(node => node.Graph.Id, StringComparer.CurrentCultureIgnoreCase).ThenBy(node => node.Graph.Id, StringComparer.Ordinal))
        {
            before.TryGetValue(node.Graph.Id, out var old);
            if (old is null || !Exact(old.Graph.VersionId, node.Graph.VersionId))
                changes.Add(new(node.Graph.Id, old, node, requested.Contains(node.Graph.Id) ? "requested" : "dependency"));
        }
        cancellation.ThrowIfCancellationRequested();
        return new(selectedNodes, changes.ToImmutable(), [.. roots], baseline, solution.Report, request.Operation);

        ImmutableArray<InventoryItem> AllTargets()
        {
            var type = InventorySelector.NormalizeType(request.Type);
            IEnumerable<InventoryItem> items = [];
            if (type is InventoryType.All or InventoryType.Mod) items = items.Concat(observation.Inventory.Mods);
            if (type is InventoryType.All or InventoryType.ResourcePack) items = items.Concat(observation.Inventory.Resources);
            if (type is InventoryType.All or InventoryType.ShaderPack) items = items.Concat(observation.Inventory.Shaders);
            return [.. items.Where(item => item.Source == ContentSource.Packwiz && !before[item.Id].Pinned)];
        }
    }
    private static bool Exact(string left, string right) => StringComparer.InvariantCulture.Equals(left, right);
    private static string RemovePrefix(string selector) => selector.StartsWith("modrinth:", StringComparison.InvariantCultureIgnoreCase) ? selector[9..] : selector;
    private static DomainException Pinned(string name, string id) => new("Compatibility.Pinned", $"'{name}' is pinned", "modpack content unpin " + id, "InvalidOperation");
    private static DomainException NoCompatible(string name) => new("Versions.NoCompatibleVersion", $"No compatible version of '{name}' exists", "check Minecraft and loader compatibility", "ObjectNotFound");
}
