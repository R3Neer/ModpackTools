using System.Collections.Immutable;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Graph;

public static class GraphReportBuilder
{
    public static GraphReport Build(GraphEnvironment environment, ImmutableArray<GraphNode> nodes,
        RuntimeOverrides? runtimeOverrides = null, GraphReportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(environment); limits ??= new();
        ValidatePolicy(limits);
        var scope = new Scope(limits, environment);
        scope.Input(nodes);
        return scope.Compose(nodes, runtimeOverrides ?? new());
    }
    private static void ValidatePolicy(GraphReportLimits limits)
    {
        var invalid = limits.MaximumNodes <= 0 ? nameof(limits.MaximumNodes)
            : limits.MaximumMods <= 0 ? nameof(limits.MaximumMods)
            : limits.MaximumAliases <= 0 ? nameof(limits.MaximumAliases)
            : limits.MaximumRequirements <= 0 ? nameof(limits.MaximumRequirements)
            : limits.MaximumWarnings <= 0 ? nameof(limits.MaximumWarnings)
            : limits.MaximumIssues <= 0 ? nameof(limits.MaximumIssues)
            : limits.MaximumFacts <= 0 ? nameof(limits.MaximumFacts)
            : limits.MaximumFactVersions <= 0 ? nameof(limits.MaximumFactVersions)
            : limits.MaximumTextCharacters <= 0 ? nameof(limits.MaximumTextCharacters)
            : limits.MaximumRequirementDepth is < 1 or > 64 ? nameof(limits.MaximumRequirementDepth)
            : limits.MaximumEvaluatedNodes <= 0 ? nameof(limits.MaximumEvaluatedNodes)
            : limits.MaximumEvaluatedVersions <= 0 ? nameof(limits.MaximumEvaluatedVersions)
            : limits.MaximumIdentityCharacters <= 0 ? nameof(limits.MaximumIdentityCharacters)
            : limits.MaximumMessageCharacters <= 0 ? nameof(limits.MaximumMessageCharacters) : null;
        if (invalid is not null) throw new DomainException("Compatibility.InvalidGraphPolicy", "Graph limits are invalid",
            "use positive graph quotas and depth between1 and64", target: invalid);
        RequirementEvaluator.ValidatePolicy(limits.Evaluation ?? new());
        _ = RequirementContext.Create([], [], limits.Context);
    }
    private static bool Is(string value, string expected) => StringComparer.InvariantCultureIgnoreCase.Equals(value, expected);
    private static bool Applies(string value, string side) => !(Is(value, "client") || Is(value, "server")) || Is(value, side);
    private static ImmutableArray<T> Items<T>(ImmutableArray<T> values) => values.IsDefault ? [] : values;
    private sealed class Scope
    {
        private readonly GraphReportLimits limits;
        private readonly GraphEnvironment environment;
        private readonly Dictionary<string, GraphNode> allNodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<GraphIssue> issues = [];
        private readonly GraphTextBudget textBudget;
        private readonly GraphIdentity identity;
        private readonly RequirementEvaluationBudget evaluationBudget;
        private readonly RequirementEvaluationLimits evaluationLimits;
        private int inputCharacters, mods, aliases, requirements, warnings, facts;
        public Scope(GraphReportLimits limits, GraphEnvironment environment)
        {
            this.limits = limits; this.environment = environment; textBudget = new(limits); identity = new(textBudget);
            evaluationBudget = new(limits.MaximumEvaluatedNodes, limits.MaximumEvaluatedVersions);
            evaluationLimits = limits.Evaluation ?? new();
        }
        private void Text(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            GraphResource.Guard(value.Length > limits.MaximumTextCharacters - inputCharacters, nameof(limits.MaximumTextCharacters)); inputCharacters += value.Length;
        }
        private static void Count(ref int count, int maximum, string name)
        { GraphResource.Guard(count >= maximum, name); count++; }
        public void Input(ImmutableArray<GraphNode> nodes)
        {
            GraphResource.Guard(!nodes.IsDefault && nodes.Length > limits.MaximumNodes, nameof(limits.MaximumNodes));
            Text(environment.MinecraftVersion); Text(environment.Loader); Text(environment.LoaderVersion); Text(environment.JavaVersion);
            foreach (var node in Items(nodes))
            {
                ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(node.Id);
                if (!allNodes.TryAdd(node.Id, node)) throw new DomainException("Content.DuplicateIdentity", "Duplicate content ID '" + node.Id + "'",
                    "supply one graph node per identity", target: node.Id);
                Text(node.Id); Text(node.VersionId); Text(node.Name); Text(node.Kind); Text(node.Side);
                foreach (var warning in Items(node.Warnings)) { Count(ref warnings, limits.MaximumWarnings, nameof(limits.MaximumWarnings)); Text(warning); }
                foreach (var requirement in Items(node.Requirements)) RequirementInput(requirement, 1);
                foreach (var artifact in Items(node.Mods))
                {
                    ArgumentNullException.ThrowIfNull(artifact); ArgumentNullException.ThrowIfNull(artifact.Declaration);
                    Count(ref mods, limits.MaximumMods, nameof(limits.MaximumMods)); var mod = artifact.Declaration;
                    Text(mod.Id); Text(mod.Version); Text(mod.Side); foreach (var entry in Items(artifact.EntryChain)) Text(entry);
                    foreach (var alias in Items(mod.Provides))
                    {
                        ArgumentNullException.ThrowIfNull(alias); ArgumentNullException.ThrowIfNull(alias.Key);
                        Count(ref aliases, limits.MaximumAliases, nameof(limits.MaximumAliases)); Text(alias.Version);
                        if (alias.Key.StringValue is { } key) Text(key); if (alias.Key.OpaqueJson is { } opaque) Text(opaque);
                    }
                    foreach (var requirement in Items(mod.Requirements)) RequirementInput(requirement, 1);
                }
            }
        }
        private void RequirementInput(ManifestRequirement value, int depth)
        {
            ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(value.Constraint);
            GraphResource.Guard(depth > limits.MaximumRequirementDepth, nameof(limits.MaximumRequirementDepth));
            Count(ref requirements, limits.MaximumRequirements, nameof(limits.MaximumRequirements));
            Text(value.Target); Text(value.Kind); Text(value.Side); Text(value.Scope); Text(value.Source); Text(value.SuggestedVersionId);
            if (value.Scheme is not (VersionScheme.General or VersionScheme.Maven)) throw new ArgumentException("Unknown requirement version scheme.");
            Text(value.Constraint.Display); Text(value.Constraint.LegacyJson); Text(value.Constraint.TypedFingerprint);
            if (!ReferenceEquals(value.Constraint.LegacyJson, value.Constraint.PublicJson)) Text(value.Constraint.PublicJson);
            foreach (var child in Items(value.Any)) RequirementInput(child, depth + 1);
            foreach (var child in Items(value.All)) RequirementInput(child, depth + 1);
            if (value.Unless is not null) RequirementInput(value.Unless, depth + 1);
        }
        public GraphReport Compose(ImmutableArray<GraphNode> nodes, RuntimeOverrides overrides)
        {
            foreach (var node in Items(nodes)) foreach (var warning in Items(node.Warnings))
                Issue(node, null, warning, null, "both", "unknown",
                    Is(warning, "Provider dependency metadata is unavailable") ? "provider.metadata-unavailable" : "verification.metadata",
                    [], false, [node.Name, ": ", warning]);
            foreach (var side in new[] { "client", "server" }) Side(nodes, side);
            if (overrides.FabricDependencies) Override("config/fabric_loader_dependencies.json");
            if (overrides.QuiltOverrides) Override("config/quilt-loader-overrides.json");
            var seen = new HashSet<(string Key, string Severity)>();
            return new([.. issues.Where(issue => seen.Add((issue.Key, issue.Severity)))
                .OrderBy(issue => issue.AuxiliarySortKey ?? issue.Key, StringComparer.Ordinal)
                .ThenBy(issue => issue.Severity, StringComparer.Ordinal).ThenBy(issue => issue.Key, StringComparer.Ordinal)]);
        }
        private void Override(string path) => Issue(null, null, null, path, "both", "unknown", "verification.runtime-override", [], false,
            ["Runtime overrides in ", path, " require manual verification"]);
        private void Side(ImmutableArray<GraphNode> nodes, string side)
        {
            var active = new List<GraphNode>(); var projects = ImmutableArray.CreateBuilder<RequirementProject>();
            var projectFacts = new Dictionary<string, ImmutableArray<string?>>(StringComparer.OrdinalIgnoreCase);
            var versions = ImmutableArray.CreateBuilder<RequirementModVersions>();
            var owners = new Dictionary<string, List<(GraphNode Node, bool Nested)>>(StringComparer.OrdinalIgnoreCase);
            void Version(string id, string value) => versions.Add(new(id, [value]));
            var builtin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["minecraft"] = environment.MinecraftVersion };
            var loaderId = Is(environment.Loader, "fabric") ? "fabricloader" : Is(environment.Loader, "quilt") ? "quilt_loader" : environment.Loader;
            builtin[loaderId] = environment.LoaderVersion;
            if (Is(environment.Loader, "forge")) { builtin["javafml"] = environment.LoaderVersion.Split('.')[0]; builtin["lowcodefml"] = "1"; }
            if (environment.JavaDeclared) builtin["java"] = environment.JavaVersion;
            foreach (var pair in builtin) Version(pair.Key, pair.Value);
            foreach (var node in Items(nodes))
            {
                if (!node.Enabled || !Applies(node.Side, side)) continue;
                active.Add(node); projects.Add(new(node.Id, node.VersionId)); projectFacts.Add(node.Id, [node.VersionId]);
                foreach (var artifact in Items(node.Mods))
                {
                    var mod = artifact.Declaration; if (!Applies(mod.Side, side)) continue;
                    if (!owners.TryGetValue(mod.Id, out var list)) { list = []; owners.Add(mod.Id, list); }
                    list.Add((node, artifact.Nested)); Version(mod.Id, mod.Version);
                    foreach (var alias in Items(mod.Provides)) if (alias.Key.StringValue is { } key) Version(key, alias.Version);
                }
            }
            var context = RequirementContext.Create(projects.ToImmutable(), versions.ToImmutable(), limits.Context);
            foreach (var pair in owners)
            {
                if (pair.Value.Count < 2) continue;
                var topLevel = pair.Value.Count(owner => !owner.Nested) > 1;
                var requirement = new ManifestRequirement(pair.Key, VersionConstraint.Text("*"), "required", "both", "mod", VersionScheme.General, "manifest", "", [], [], null);
                Issue(pair.Value[0].Node, requirement, null, null, side, topLevel ? "error" : "unknown",
                    topLevel ? "conflict.duplicate-id" : "verification.bundled-selection", [], false,
                    [topLevel ? "Duplicate top-level mod ID '" : "Multiple bundled versions of '", pair.Key,
                        topLevel ? "' [" : "' require runtime selection [", side, "]"]);
            }
            var unverifiedMod = active.Any(node => Is(node.Kind, "mod") && node.Mods.IsDefaultOrEmpty);
            foreach (var node in active)
            {
                foreach (var requirement in Items(node.Requirements)) Evaluate(node, requirement);
                foreach (var artifact in Items(node.Mods)) if (Applies(artifact.Declaration.Side, side))
                    foreach (var requirement in Items(artifact.Declaration.Requirements)) Evaluate(node, requirement);
            }
            void Evaluate(GraphNode node, ManifestRequirement requirement)
            {
                if (!Applies(requirement.Side, side)) return;
                if (Is(requirement.Scope, "project") && Is(requirement.Source, "provider")
                    && allNodes.TryGetValue(requirement.Target, out var target) && !Applies(target.Side, side)) return;
                var valid = RequirementEvaluator.EvaluateValidated(requirement, context, evaluationLimits, evaluationBudget);
                if (valid == true) return;
                if (valid == false && Is(requirement.Scope, "mod") && requirement.Target.Length != 0
                    && !context.Mods.ContainsKey(requirement.Target) && unverifiedMod) valid = null;
                var severity = valid is null ? "unknown" : Is(requirement.Kind, "recommended") || Is(requirement.Kind, "discouraged") ? "warning" : "error";
                var code = severity == "error" ? "conflict.required" : severity == "warning"
                    ? Is(requirement.Kind, "discouraged") ? "recommendation.discouraged" : "recommendation.optional"
                    : Is(requirement.Target, "java") && !context.Mods.ContainsKey("java") ? "environment.java-undeclared"
                    : Is(requirement.Scope, "project") && requirement.SuggestedVersionId.Length != 0 ? "provider.version-differs"
                    : context.Mods.ContainsKey(requirement.Target) ? "version-range.unsupported" : "verification.requirement";
                var evidence = ImmutableArray.CreateBuilder<GraphFact>(); Targets(requirement);
                var suggested = requirement.SuggestedVersionId.Length != 0;
                Issue(node, requirement, null, null, side, severity, code, evidence.ToImmutable(), true,
                    [node.Name, ": ", requirement.Kind, " ", requirement.Target, " ", suggested ? "provider version " : "",
                        suggested ? requirement.SuggestedVersionId : requirement.RangeDisplay, " [", side, "]"]);
                void Targets(ManifestRequirement value)
                {
                    if (value.Target.Length != 0)
                    {
                        Count(ref facts, limits.MaximumFacts, nameof(limits.MaximumFacts));
                        var project = Is(value.Scope, "project");
                        var projectPresent = projectFacts.TryGetValue(value.Target, out var projectVersions);
                        var modPresent = context.Mods.TryGetValue(value.Target, out var modVersions);
                        evidence.Add(new(value.Scope, value.Target, project ? projectPresent : modPresent,
                            project ? projectPresent ? projectVersions : [] : modPresent ? modVersions : [],
                            project && projectPresent ? projectVersions : modPresent ? modVersions : []));
                    }
                    foreach (var child in Items(value.Any)) Targets(child);
                    foreach (var child in Items(value.All)) Targets(child);
                    if (value.Unless is not null) Targets(value.Unless);
                }
            }
        }
        private void Issue(GraphNode? owner, ManifestRequirement? requirement, string? warning, string? runtimeOverride,
            string side, string severity, string code, ImmutableArray<GraphFact> evidence, bool includeFacts, string[] messageParts)
        {
            GraphResource.Guard(issues.Count >= limits.MaximumIssues, nameof(limits.MaximumIssues));
            var length = messageParts.Sum(part => (long)part.Length);
            GraphResource.Guard(length > limits.MaximumMessageCharacters, nameof(limits.MaximumMessageCharacters));
            textBudget.Message((int)length); var message = string.Concat(messageParts);
            var key = identity.Modern(owner, side, requirement is not null ? (byte)1 : runtimeOverride is not null ? (byte)2 : (byte)0,
                requirement, runtimeOverride ?? warning, evidence, includeFacts);
            var auxiliary = identity.Auxiliary(owner, side, requirement, warning, runtimeOverride, evidence, includeFacts);
            issues.Add(new(key, auxiliary, code, owner, requirement, warning, side, severity, message));
        }
    }
}
