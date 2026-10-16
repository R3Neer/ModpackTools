using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Resolution;

public static class DependencyResolver
{
    public static async Task<DependencySearchResult> SolveAsync(ResolutionInput input, IResolutionCandidates candidates,
        ResolutionLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(candidates);
        try { return await new Search(input, candidates, limits ?? new(), cancellation).Run().ConfigureAwait(false); }
        catch (OperationCanceledException)
        { throw new DomainException("Core.OperationCancelled", "Dependency search was cancelled", "retry when ready", "OperationStopped"); }
    }

    // Callers supply owned nodes already validated at the acquisition boundary.
    // SolveAsync validates and charges these inputs before invoking the comparison.
    public static bool IsDowngrade(ResolutionNode before, ResolutionNode after)
        => Downgrade(before, after, () => { });

    internal static void ValidatePolicy(ResolutionLimits limits)
    {
        var invalid = limits.MaximumStates <= 0 ? nameof(limits.MaximumStates)
            : limits.MaximumDomains <= 0 ? nameof(limits.MaximumDomains)
            : limits.MaximumCandidateVersions <= 0 ? nameof(limits.MaximumCandidateVersions)
            : limits.MaximumCandidateNodes <= 0 ? nameof(limits.MaximumCandidateNodes)
            : limits.MaximumWork <= 0 ? nameof(limits.MaximumWork)
            : limits.MaximumIdentityBytes <= 0 ? nameof(limits.MaximumIdentityBytes)
            : limits.MaximumRetainedCharacters <= 0 ? nameof(limits.MaximumRetainedCharacters) : null;
        if (invalid is not null) throw new DomainException("Compatibility.InvalidSearchPolicy", "Dependency search limits must be positive", "use positive search quotas", target: invalid);
        GraphReportBuilder.ValidatePolicy(limits.Graph ?? new());
    }

    private static bool Downgrade(ResolutionNode before, ResolutionNode after, Action charge)
    {
        var compared = false;
        var alternatives = new Dictionary<string, ArtifactMod>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var artifact in Items(after.Graph.Mods)) { charge(); alternatives.TryAdd(artifact.Declaration.Id, artifact); }
        foreach (var old in Items(before.Graph.Mods))
        {
            charge();
            if (!alternatives.TryGetValue(old.Declaration.Id, out var next)) continue;
            var comparison = VersionOrdering.Compare(next.Declaration.Version, old.Declaration.Version, before.Maven ? VersionScheme.Maven : VersionScheme.General);
            if (comparison is null) continue;
            compared = true; if (comparison < 0) return true;
        }
        return !compared && before.OrderingDate is { } previous && after.OrderingDate is { } current && current.Ticks < previous.Ticks;
    }

    private static bool Is(string left, string right) => StringComparer.InvariantCultureIgnoreCase.Equals(left, right);
    private static ImmutableArray<T> Items<T>(ImmutableArray<T> values) => values.IsDefault ? [] : values;
    private static DomainException Limit(string property) => new("Compatibility.SearchLimit", "Dependency search exceeded its resource limit; no changes were applied",
        "narrow the requested batch or pin a known compatible dependency", "LimitsExceeded", property);

    private sealed record State(ImmutableDictionary<string, ResolutionNode> Nodes, ImmutableArray<string> Order)
    {
        public State With(string id, ResolutionNode value) => new(Nodes.SetItem(id, value), Nodes.ContainsKey(id) ? Order : Order.Add(id));
        public ImmutableArray<ResolutionNode> Values => [.. Order.Select(id => Nodes[id])];
    }
    private sealed class Frame(State state)
    {
        public State State { get; } = state;
        public bool Entered { get; set; }
        public ImmutableArray<string> Ids { get; set; } = [];
        public int IdIndex { get; set; }
        public ResolutionDomain? Domain { get; set; }
        public int VersionIndex { get; set; }
    }

    private sealed class Search
    {
        private readonly ResolutionInput input;
        private readonly IResolutionCandidates source;
        private readonly ResolutionLimits limits;
        private readonly GraphReportLimits graphLimits;
        private readonly CancellationToken cancellation;
        private readonly Dictionary<string, ResolutionDomain> domains = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, ResolutionNode>> candidateNodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> visited = new(StringComparer.Ordinal);
        private readonly HashSet<string> roots;
        private readonly HashSet<string> baseline;
        private readonly State before, initial;
        private long work, identityBytes, retainedCharacters;
        private int versions, nodeCount;
        private DependencySolution? best;
        private GraphReport? last;

        public Search(ResolutionInput input, IResolutionCandidates source, ResolutionLimits limits, CancellationToken cancellation)
        {
            this.input = input; this.source = source; this.limits = limits; this.cancellation = cancellation;
            ValidatePolicy(limits); graphLimits = limits.Graph ?? new();
            before = Own(input.Before); initial = Own(input.Initial);
            last = input.PreviousReport;
            roots = new(StringComparer.OrdinalIgnoreCase); baseline = new(StringComparer.OrdinalIgnoreCase);
            foreach (var id in Items(input.Roots)) { Charge(); Text(id); roots.Add(id); }
            foreach (var key in Items(input.BaselineKeys)) { Charge(); Text(key); baseline.Add(key); }
        }

        public async Task<DependencySearchResult> Run()
        {
            var stack = new Stack<Frame>(); stack.Push(new(initial));
            while (stack.TryPeek(out var frame))
            {
                cancellation.ThrowIfCancellationRequested(); Charge();
                if (!frame.Entered)
                {
                    var key = Identity(frame.State);
                    if (visited.Contains(key)) { stack.Pop(); continue; }
                    if (visited.Count >= limits.MaximumStates) throw Limit(nameof(limits.MaximumStates));
                    visited.Add(key); frame.Entered = true;
                    var report = await Report(frame.State).ConfigureAwait(false);
                    var issue = report.Errors.FirstOrDefault(value => input.Strict || !baseline.Contains(value.Key));
                    if (issue is null)
                    {
                        if (input.Strict && !report.Unknown.IsEmpty) last = report;
                        else
                        {
                            var cost = Cost(frame.State);
                            if (best is null || cost.CompareTo(best.Cost) < 0) best = new(frame.State.Values, report, cost, visited.Count);
                        }
                        stack.Pop(); continue;
                    }
                    last = report; frame.Ids = Targets(frame.State, issue);
                }
                if (frame.IdIndex >= frame.Ids.Length) { stack.Pop(); continue; }
                var id = frame.Ids[frame.IdIndex];
                if (roots.Contains(id) || before.Nodes.TryGetValue(id, out var installed) && (installed.Pinned || installed.Local))
                { frame.IdIndex++; frame.Domain = null; continue; }
                frame.Domain ??= await Domain(id).ConfigureAwait(false);
                if (frame.VersionIndex >= frame.Domain.VersionIds.Length)
                { frame.IdIndex++; frame.Domain = null; frame.VersionIndex = 0; continue; }
                var version = frame.Domain.VersionIds[frame.VersionIndex++];
                Charge();
                if (frame.State.Nodes.TryGetValue(id, out var current) && current.Graph.VersionId.Equals(version, StringComparison.InvariantCulture)) continue;
                var candidate = await Node(id, version).ConfigureAwait(false);
                if (!input.AllowDowngrade && before.Nodes.TryGetValue(id, out installed) && Downgrade(installed, candidate, () => Charge())) continue;
                stack.Push(new(frame.State.With(id, candidate)));
            }
            return new(best is null ? null : best with { VisitedStates = visited.Count }, last, visited.Count);
        }

        private async Task<GraphReport> Report(State state)
        {
            Charge(state.Order.Length);
            var report = GraphReportBuilder.BuildWithWork(input.Environment, [.. state.Order.Select(id => state.Nodes[id].Graph)], input.RuntimeOverrides, graphLimits, Charge);
            var issues = ImmutableArray.CreateBuilder<GraphIssue>(report.Issues.Length);
            long characters = report.Issues.Sum(issue => (long)issue.Message.Length);
            foreach (var issue in report.Issues)
            {
                Charge(); var value = issue;
                if (issue.Severity == "error" && issue.Requirement is { } requirement && Is(requirement.Scope, "project") && Is(requirement.Source, "provider") && !state.Nodes.ContainsKey(requirement.Target))
                {
                    var domain = await Domain(requirement.Target).ConfigureAwait(false);
                    if (domain.Availability == CandidateAvailability.KnownEmpty)
                    {
                        const string suffix = "; provider metadata exposes no compatible candidate";
                        if (suffix.Length > graphLimits.MaximumMessageCharacters - characters) throw Limit("MaximumMessageCharacters");
                        characters += suffix.Length;
                        value = issue.Reclassify("provider.no-compatible-candidate", "unknown", issue.Message + suffix);
                    }
                }
                issues.Add(value);
            }
            return new(issues.ToImmutable());
        }

        private async ValueTask<ResolutionDomain> Domain(string id)
        {
            if (domains.TryGetValue(id, out var cached)) return cached;
            Charge(); if (domains.Count >= limits.MaximumDomains) throw Limit(nameof(limits.MaximumDomains));
            Text(id);
            var domain = id.StartsWith("modrinth:", StringComparison.Ordinal) ? await source.GetCandidatesAsync(id, cancellation).ConfigureAwait(false) : new(CandidateAvailability.Unknown, []);
            ArgumentNullException.ThrowIfNull(domain);
            var ids = Items(domain.VersionIds);
            if (!Enum.IsDefined(domain.Availability) || domain.Availability == CandidateAvailability.KnownEmpty && !ids.IsEmpty || domain.Availability == CandidateAvailability.KnownAvailable && ids.IsEmpty)
                throw new DomainException("Compatibility.InvalidCandidateDomain", "Candidate availability contradicts the domain", "supply consistent owned candidate evidence", "InvalidData", id);
            if (ids.Length > limits.MaximumCandidateVersions - versions) throw Limit(nameof(limits.MaximumCandidateVersions));
            versions += ids.Length;
            foreach (var version in ids) { Charge(); Text(version); }
            domain = domain with { VersionIds = ids }; domains.Add(id, domain); return domain;
        }

        private async ValueTask<ResolutionNode> Node(string id, string version)
        {
            if (candidateNodes.TryGetValue(id, out var values) && values.TryGetValue(version, out var existing)) return existing;
            if (nodeCount >= limits.MaximumCandidateNodes) throw Limit(nameof(limits.MaximumCandidateNodes));
            var node = await source.GetNodeAsync(id, version, cancellation).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(node.Graph);
            if (!StringComparer.OrdinalIgnoreCase.Equals(id, node.Graph.Id) || !StringComparer.Ordinal.Equals(version, node.Graph.VersionId))
                throw new DomainException("Compatibility.CandidateIdentityMismatch", "Candidate node does not match its requested identity/version", "supply stable owned candidate evidence", "InvalidData", id);
            Retain(node); nodeCount++;
            values ??= new(StringComparer.Ordinal); values.Add(version, node); candidateNodes[id] = values; return node;
        }

        private State Own(ImmutableArray<ResolutionNode> values)
        {
            var nodes = ImmutableDictionary.CreateBuilder<string, ResolutionNode>(StringComparer.OrdinalIgnoreCase);
            var order = ImmutableArray.CreateBuilder<string>();
            if (Items(values).Length > graphLimits.MaximumNodes) throw Limit("MaximumNodes");
            foreach (var node in Items(values))
            {
                Retain(node);
                if (!nodes.TryAdd(node.Graph.Id, node)) throw new DomainException("Content.DuplicateIdentity", "Duplicate content ID '" + node.Graph.Id + "'", "supply one owned node per identity", "InvalidData", node.Graph.Id);
                order.Add(node.Graph.Id);
            }
            return new(nodes.ToImmutable(), order.ToImmutable());
        }

        private void Retain(ResolutionNode node)
        {
            ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(node.Graph);
            var graph = node.Graph;
            // Validate declaration depth/shape and existing graph quotas at the boundary.
            GraphReportBuilder.ValidateInput(input.Environment, [graph], graphLimits, Charge);
            Text(graph.Id); Text(graph.VersionId); Text(graph.Name); Text(graph.Kind); Text(graph.Side);
            foreach (var warning in Items(graph.Warnings)) { Charge(); Text(warning); }
            foreach (var artifact in Items(graph.Mods))
            {
                Charge(); Text(artifact.Declaration.Id); Text(artifact.Declaration.Version); Text(artifact.Declaration.Side);
                foreach (var alias in Items(artifact.Declaration.Provides)) { Charge(); Text(alias.Key.StringValue ?? alias.Key.OpaqueJson ?? ""); Text(alias.Version); }
                foreach (var requirement in Requirements(artifact.Declaration.Requirements)) RetainRequirement(requirement);
                foreach (var path in Items(artifact.EntryChain)) Text(path);
            }
            foreach (var requirement in Requirements(graph.Requirements)) RetainRequirement(requirement);
        }
        private void RetainRequirement(ManifestRequirement requirement)
        {
            Text(requirement.Target); Text(requirement.Kind); Text(requirement.Side); Text(requirement.Scope); Text(requirement.Source); Text(requirement.SuggestedVersionId);
            Text(requirement.Constraint.PublicJson); Text(requirement.Constraint.LegacyJson); Text(requirement.Constraint.TypedFingerprint); Text(requirement.Constraint.Display);
        }

        private IEnumerable<ManifestRequirement> Requirements(ImmutableArray<ManifestRequirement> roots)
        {
            var stack = new Stack<ManifestRequirement>();
            for (var index = Items(roots).Length - 1; index >= 0; index--) stack.Push(roots[index]);
            while (stack.TryPop(out var value))
            {
                Charge(); yield return value;
                if (value.Unless is not null) stack.Push(value.Unless);
                for (var index = Items(value.All).Length - 1; index >= 0; index--) stack.Push(value.All[index]);
                for (var index = Items(value.Any).Length - 1; index >= 0; index--) stack.Push(value.Any[index]);
            }
        }
        private ImmutableArray<string> Targets(State state, GraphIssue issue)
        {
            var ids = new List<string> { issue.Owner };
            foreach (var requirement in Requirements(issue.Requirement is { } root ? [root] : []))
            {
                if (string.IsNullOrEmpty(requirement.Target)) continue;
                if (Is(requirement.Scope, "project")) { ids.Add(requirement.Target); continue; }
                var direct = new List<string>();
                foreach (var id in state.Order)
                {
                    Charge(); var node = state.Nodes[id];
                    foreach (var artifact in Items(node.Graph.Mods))
                    {
                        Charge(); var mod = artifact.Declaration;
                        if (Is(mod.Id, requirement.Target) || Alias(mod, requirement.Target))
                        { direct.Add(id); break; }
                    }
                }
                ids.InsertRange(0, direct);
                if (direct.Count == 0 && state.Nodes.TryGetValue(issue.Owner, out var owner))
                    foreach (var dependency in Items(owner.Graph.Requirements))
                    { Charge(); if (Is(dependency.Scope, "project") && Is(dependency.Kind, "required")) ids.Add(dependency.Target); }
            }
            var unique = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            return [.. ids.Where(id => !string.IsNullOrEmpty(id) && unique.Add(id))];
            bool Alias(DeclaredMod mod, string target)
            {
                foreach (var alias in Items(mod.Provides))
                { Charge(); if (alias.Key.StringValue is { } key && StringComparer.OrdinalIgnoreCase.Equals(key, target)) return true; }
                return false;
            }
        }

        private ResolutionCost Cost(State state)
        {
            var changed = 0; var explicitChanged = 0; var added = 0; decimal freshness = 0;
            foreach (var id in state.Order)
            {
                Charge(); var node = state.Nodes[id]; freshness += node.OrderingDate?.Ticks ?? 0;
                if (!before.Nodes.TryGetValue(id, out var previous)) { added++; continue; }
                if (node.Graph.VersionId.Equals(previous.Graph.VersionId, StringComparison.InvariantCulture)) continue;
                changed++; if (previous.Explicit) explicitChanged++;
            }
            return new(changed, explicitChanged, added, -freshness);
        }

        private string Identity(State state)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var id in state.Order.Order(StringComparer.OrdinalIgnoreCase))
            { Charge(); Append(id.ToUpperInvariant()); Append(state.Nodes[id].Graph.VersionId); }
            return Convert.ToHexString(hash.GetHashAndReset());
            void Append(string text)
            {
                var count = 2L * text.Length;
                if (count + 4 > limits.MaximumIdentityBytes - identityBytes) throw Limit(nameof(limits.MaximumIdentityBytes));
                identityBytes += count + 4;
                Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, text.Length); hash.AppendData(length);
                Span<byte> buffer = stackalloc byte[1024];
                for (var offset = 0; offset < text.Length; offset += buffer.Length / 2)
                {
                    var characters = Math.Min(buffer.Length / 2, text.Length - offset);
                    for (var index = 0; index < characters; index++) BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(index * 2, 2), text[offset + index]);
                    hash.AppendData(buffer[..(characters * 2)]);
                }
            }
        }
        private void Charge(long count = 1)
        { cancellation.ThrowIfCancellationRequested(); if (count > limits.MaximumWork - work) throw Limit(nameof(limits.MaximumWork)); work += count; }
        private void Text(string text)
        { ArgumentNullException.ThrowIfNull(text); if (text.Length > limits.MaximumRetainedCharacters - retainedCharacters) throw Limit(nameof(limits.MaximumRetainedCharacters)); retainedCharacters += text.Length; }
    }
}
