using System.Collections.Immutable;
using System.Text;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;

namespace ModpackTools.Core.Resolution;

public sealed record RemovalPlanRequest(ImmutableArray<string> Selectors, string Type = "all", bool Cascade = false, bool AutoRemove = false, bool Strict = false);
public sealed record RemovalChange(string Id, ProjectContentNode Before, string Reason);
public sealed record RemovalPlan(ImmutableArray<string> Requested, ImmutableArray<ProjectContentNode> Nodes,
    GraphReport Baseline, GraphReport Report, ImmutableArray<RemovalChange> Changes);
public sealed record RemovalPlanningLimits(GraphReportLimits? Graph = null, int MaximumGraphPasses = 1024,
    long MaximumWork = 100_000_000, int MaximumEdges = 1_000_000, int MaximumDetailCharacters = 1024 * 1024);

public static class RemovalPlanning
{
    public static RemovalPlan Prepare(GraphEnvironment environment, ImmutableArray<ProjectContentNode> nodes, RemovalPlanRequest request,
        RuntimeOverrides? runtimeOverrides = null, RemovalPlanningLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(environment); limits ??= new();
        if (limits.MaximumGraphPasses <= 0 || limits.MaximumWork <= 0 || limits.MaximumEdges <= 0 || limits.MaximumDetailCharacters <= 0)
            throw new DomainException("Remove.InvalidPlanningPolicy", "Removal planning quotas must be positive", "use positive bounded planning quotas");
        var graphLimits = limits.Graph ?? new(); GraphReportBuilder.ValidatePolicy(graphLimits);
        if (nodes.IsDefault || request.Selectors.IsDefaultOrEmpty || request.Selectors.Length > 1024
            || request.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > 4096))
            throw new DomainException("Remove.InvalidRequest", "Removal requires bounded nonempty selectors and owned nodes", "select physical installed content");
        long work = 0; var passes = 0;
        try
        {
            Charge(0);
            GraphResource.Guard(nodes.Length > graphLimits.MaximumNodes, nameof(graphLimits.MaximumNodes));
            long physicalCharacters = 0;
            foreach (var node in nodes)
            {
                Charge(1);
                if (node is null || node.Graph is null || node.Item is null || node.Intent is null
                    || node.Item.Id is null || node.Item.Name is null || node.Item.Side is null
                    || node.Graph.Id != node.Item.Id || node.Graph.Name != node.Item.Name || node.Graph.Side != node.Item.Side)
                    throw new DomainException("Remove.InvalidState", "Removal nodes must retain matching physical and graph identities", "observe the project again", "InvalidData");
                physicalCharacters += (long)node.Item.Name.Length + node.Item.Id.Length + node.Item.Side.Length + node.Intent.Length + (node.Item.Filename?.Length ?? 0) + (node.Item.MetadataPath?.Length ?? 0);
                if (physicalCharacters > graphLimits.MaximumTextCharacters) throw Limit(nameof(graphLimits.MaximumTextCharacters));
            }
            GraphReportBuilder.ValidateInput(environment, [.. nodes.Select(node => node.Graph)], graphLimits, Charge);
            var type = InventorySelector.NormalizeType(request.Type);
            var all = new Dictionary<string, ProjectContentNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                Charge(1);
                if (!all.TryAdd(node.Graph.Id, node)) throw new DomainException("Content.DuplicateIdentity", $"Duplicate content ID '{node.Graph.Id}'", "resolve duplicate physical metadata", "InvalidData", node.Graph.Id);
            }
            var requested = RemovalSelector.ResolveItems([.. nodes.Select(node => node.Item).Where(item => type == InventoryType.All ||
                type == InventoryType.Mod && item.Kind == ContentKind.Mod || type == InventoryType.ResourcePack && item.Kind == ContentKind.ResourcePack || type == InventoryType.ShaderPack && item.Kind == ContentKind.ShaderPack)],
                request.Selectors, Charge, values => Detail(values, ", "));
            var remaining = new Dictionary<string, ProjectContentNode>(all, StringComparer.OrdinalIgnoreCase);
            var baseline = Report(); var accepted = baseline.Errors.Select(issue => issue.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = ImmutableArray.CreateBuilder<RemovalChange>(); var removedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in requested) Remove(id, "requested");
            while (true)
            {
                var report = Report(); var errors = report.Errors.Where(issue => !accepted.Contains(issue.Key)).ToArray();
                if (errors.Length == 0) break;
                if (!request.Cascade) throw new DomainException("Remove.Dependents", "Removal would break installed dependents", "include the dependents explicitly, or preview remove --cascade --dry-run", "InvalidOperation", details: Detail(errors.Select(issue => issue.Message), "; "));
                var owners = errors.Where(issue => issue.Code == "conflict.required" && Is(issue.Requirement?.Kind, "required") && remaining.ContainsKey(issue.Owner))
                    .Select(issue => issue.Owner).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value, StringComparer.Ordinal).ToArray();
                if (owners.Length == 0) GraphPolicy.Assert(report, baseline, limits: new(limits.MaximumDetailCharacters));
                foreach (var id in owners) Remove(id, "dependent");
            }
            if (request.AutoRemove)
            {
                if (!baseline.Unknown.IsEmpty) throw new DomainException("Remove.IncompleteVerification", "Automatic dependency removal requires complete verification", "resolve incomplete metadata, or omit --autoremove and remove explicit selectors only", "InvalidOperation", details: Detail(baseline.Unknown.Select(issue => issue.Message), "; "));
                var edges = Edges(); var affected = Reach(removedIds, null);
                var roots = remaining.Values.Where(node => !Is(node.Intent, "transitive") || node.Pinned || node.Item.Source != ContentSource.Packwiz || !affected.Contains(node.Graph.Id)).Select(node => node.Graph.Id);
                var retained = Reach(roots, removedIds);
                foreach (var id in remaining.Keys.Order(StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value, StringComparer.Ordinal).ToArray())
                    if (!retained.Contains(id)) Remove(id, "unused dependency");

                HashSet<string> Reach(IEnumerable<string> roots, HashSet<string>? excluded)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var pending = new Stack<string>();
                    foreach (var id in roots) { Charge(1); pending.Push(id); }
                    while (pending.TryPop(out var id))
                    {
                        Charge(1); if (excluded?.Contains(id) == true || !seen.Add(id)) continue;
                        if (edges.TryGetValue(id, out var targets)) foreach (var target in targets) { Charge(1); pending.Push(target); }
                    }
                    return seen;
                }
            }
            foreach (var change in removed)
                if (change.Before.Pinned) throw new DomainException("Compatibility.Pinned", $"'{change.Before.Item.Name}' is pinned", "modpack content unpin " + change.Id, "InvalidOperation");
            var final = Report(); GraphPolicy.Assert(final, baseline, request.Strict, limits: new(limits.MaximumDetailCharacters));
            return new(requested, remaining.Values.ToImmutableArray(), baseline, final, removed.ToImmutable());

            void Remove(string id, string reason)
            {
                Charge(1);
                if (!remaining.Remove(id, out var node)) throw new DomainException("Remove.InvalidState", "Removal selected an absent identity", "observe the project again", "InvalidData", id);
                removedIds.Add(node.Graph.Id); removed.Add(new(node.Graph.Id, node, reason));
            }
            GraphReport Report()
            {
                if (++passes > limits.MaximumGraphPasses) throw Limit(nameof(limits.MaximumGraphPasses));
                return GraphReportBuilder.BuildWithWork(environment, remaining.Values.Select(node => node.Graph).ToImmutableArray(), runtimeOverrides, graphLimits, Charge);
            }
            Dictionary<string, HashSet<string>> Edges()
            {
                var providers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); var count = 0;
                foreach (var node in nodes)
                    foreach (var mod in node.Graph.Mods.IsDefault ? [] : node.Graph.Mods)
                    {
                        Provider(mod.Declaration.Id, node.Graph.Id);
                        if (!mod.Declaration.Provides.IsDefault) foreach (var alias in mod.Declaration.Provides)
                            if (alias.Key.Kind == ManifestAliasKeyKind.String) Provider(alias.Key.StringValue!, node.Graph.Id);
                    }
                var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in nodes)
                {
                    var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); result.Add(node.Graph.Id, targets);
                    var requirements = (node.Graph.Requirements.IsDefault ? [] : node.Graph.Requirements)
                        .Concat((node.Graph.Mods.IsDefault ? [] : node.Graph.Mods).SelectMany(mod => mod.Declaration.Requirements.IsDefault ? [] : mod.Declaration.Requirements));
                    foreach (var requirement in requirements)
                    {
                        Charge(1);
                        if (Is(requirement.Kind, "incompatible") || Is(requirement.Kind, "discouraged")) continue;
                        var pending = new Stack<ManifestRequirement>(); pending.Push(requirement);
                        while (pending.TryPop(out var leaf))
                        {
                            Charge(1);
                            if (!string.IsNullOrEmpty(leaf.Target))
                            {
                                if (Is(leaf.Scope, "project")) { if (all.ContainsKey(leaf.Target)) Edge(leaf.Target); }
                                else if (providers.TryGetValue(leaf.Target, out var found)) foreach (var id in found) Edge(id);
                            }
                            if (!leaf.Any.IsDefault) foreach (var child in leaf.Any) pending.Push(child);
                            if (!leaf.All.IsDefault) foreach (var child in leaf.All) pending.Push(child);
                            if (leaf.Unless is not null) pending.Push(leaf.Unless);
                        }
                    }
                    void Edge(string id) { Charge(1); if (targets.Add(id) && ++count > limits.MaximumEdges) throw Limit(nameof(limits.MaximumEdges)); }
                }
                return result;
                void Provider(string alias, string owner)
                {
                    Charge(1);
                    if (!providers.TryGetValue(alias, out var values)) providers.Add(alias, values = new(StringComparer.OrdinalIgnoreCase));
                    if (values.Add(owner) && ++count > limits.MaximumEdges) throw Limit(nameof(limits.MaximumEdges));
                }
            }
            string Detail(IEnumerable<string> values, string separator)
            {
                var text = new StringBuilder();
                foreach (var value in values)
                {
                    Charge(1);
                    if (value.Length + (text.Length == 0 ? 0 : separator.Length) > limits.MaximumDetailCharacters - text.Length) throw Limit(nameof(limits.MaximumDetailCharacters));
                    if (text.Length != 0) text.Append(separator); text.Append(value);
                }
                return text.ToString();
            }
        }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Removal planning was cancelled", "retry when ready", "OperationStopped"); }
        void Charge(long amount)
        {
            cancellation.ThrowIfCancellationRequested();
            if (amount < 0 || amount > limits.MaximumWork - work) throw Limit(nameof(limits.MaximumWork)); work += amount;
        }
    }
    private static bool Is(string? value, string expected) => StringComparer.InvariantCultureIgnoreCase.Equals(value, expected);
    private static DomainException Limit(string property) => new("Remove.PlanningLimitExceeded", "Removal planning exceeds its resource quota", "use explicit bounded removal quotas", "LimitsExceeded", property);
}
