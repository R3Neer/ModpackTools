using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Versions;

internal static class GraphContracts
{
    public static void Verify()
    {
        var environment = new GraphEnvironment("1.21", "fabric", "0.16");
        ManifestRequirement Requirement(string target, VersionConstraint? constraint = null, string scope = "mod", string source = "manifest")
            => new(target, constraint ?? VersionConstraint.Text("*"), "required", "both", scope, VersionScheme.General, source, "", [], [], null);
        GraphNode Node(string id, ImmutableArray<ArtifactMod> mods = default, ImmutableArray<ManifestRequirement> requirements = default)
            => new(id, "v1", id, "mod", "both", true, mods, requirements, []);
        ArtifactMod Mod(string id, string version, bool nested = false, ImmutableArray<ManifestAlias> aliases = default)
            => new(new(id, version, "both", aliases, []), nested, []);
        void Limit(string target, Action action)
        {
            try { action(); throw new InvalidOperationException("Graph quota was not enforced: " + target); }
            catch (DomainException exception) when (exception.Id == "ModpackTools.Compatibility.GraphLimitExceeded" && exception.Target is TextTarget { Value: var actual } && actual == target) { }
        }
        if (!GraphReportBuilder.Build(environment, default).Complete) throw new InvalidOperationException("Empty graph is incomplete.");
        var errors = GraphReportBuilder.Build(environment, [Node("a", [Mod("m", "1")]), Node("b", [Mod("m", "2")])]);
        if (errors.Errors.Length != 2 || !errors.Complete || errors.Issues.Any(issue => issue.Code != "conflict.duplicate-id" || issue.Owner != "a"))
            throw new InvalidOperationException("Native duplicate ownership or complete/error distinction changed.");
        var bundled = GraphReportBuilder.Build(environment, [Node("a", [Mod("m", "1")]), Node("b", [Mod("m", "2", true)])]);
        if (bundled.Unknown.Length != 2 || bundled.Complete || bundled.Issues.Any(issue => issue.Code != "verification.bundled-selection"))
            throw new InvalidOperationException("Bundled occurrences were silently selected.");
        var aliasGraph = GraphReportBuilder.Build(environment, [Node("a", [Mod("m", "1", aliases: [new(ManifestAliasKey.FromString("Keys"), "1")])], [Requirement("Keys")])]);
        if (!aliasGraph.Issues.IsEmpty) throw new InvalidOperationException("Keys alias was not indexed as data.");
        var target = Node("target") with { Side = "server", Enabled = false };
        var provider = GraphReportBuilder.Build(environment, [Node("a", [Mod("m", "1")], [Requirement("target", scope: "project", source: "provider")]), target]);
        if (provider.Errors is not [{ Side: "server" }]) throw new InvalidOperationException("Provider off-side skip lost disabled target evidence.");
        var warning = Node("w") with { Name = "ab", VersionId = "", Enabled = false, Warnings = ["x"] };
        var warningGraph = GraphReportBuilder.Build(environment, [warning], limits: new(MaximumMessageCharacters: 5, MaximumIdentityCharacters: 17));
        if (warningGraph.Unknown is not [{ Side: "both", WarningRequirement: "x", Requirement: null, Message: "ab: x" }])
            throw new InvalidOperationException("Disabled node warning or closed issue payload changed.");
        Limit("MaximumMessageCharacters", () => GraphReportBuilder.Build(environment, [warning], limits: new(MaximumMessageCharacters: 4)));
        Limit("MaximumIdentityCharacters", () => GraphReportBuilder.Build(environment, [warning], limits: new(MaximumIdentityCharacters: 16)));
        var satisfied = ImmutableArray.Create(Node("a", [Mod("m", "1")], [Requirement("m")]));
        if (!GraphReportBuilder.Build(environment, satisfied, limits: new(MaximumEvaluatedNodes: 2, MaximumEvaluatedVersions: 2)).Complete)
            throw new InvalidOperationException("Exact aggregate evaluation quotas rejected short-circuited healthy graph.");
        Limit("MaximumEvaluatedNodes", () => GraphReportBuilder.Build(environment, satisfied, limits: new(MaximumEvaluatedNodes: 1)));
        Limit("MaximumEvaluatedVersions", () => GraphReportBuilder.Build(environment, satisfied, limits: new(MaximumEvaluatedVersions: 1)));
        var falseRange = ImmutableArray.Create(Node("a", [Mod("m", "1")], [Requirement("m", VersionConstraint.Text(">2"))]));
        if (GraphReportBuilder.Build(environment, falseRange, limits: new(MaximumFacts: 2, MaximumFactVersions: 4)).Errors.Length != 2)
            throw new InvalidOperationException("Exact fact quotas rejected known conflicts.");
        Limit("MaximumFacts", () => GraphReportBuilder.Build(environment, falseRange, limits: new(MaximumFacts: 1)));
        Limit("MaximumFactVersions", () => GraphReportBuilder.Build(environment, falseRange, limits: new(MaximumFactVersions: 3)));
        var fabricated = RootManifestParser.Parse("{schemaVersion:1,id:'m',version:'1',depends:{d:NaN}}", null, "fabric").Mods[0].Requirements[0];
        var text = fabricated with { Constraint = VersionConstraint.Text("NaN") };
        var collision = GraphReportBuilder.Build(environment, [Node("a", [Mod("m", "1")], [fabricated, text]), Node("b", [Mod("d", "0")])]);
        if (collision.Issues.Length != 4 || collision.Issues.Select(issue => issue.Key).Distinct().Count() != 4 || collision.Unknown.Length != 2 || collision.Errors.Length != 2)
            throw new InvalidOperationException("Raw-type constraint collision erased distinct diagnostics.");
        var unobserved = GraphReportBuilder.Build(environment, [Node("a", [Mod("", "1", true), Mod("", "2", true)], [Requirement("")])]);
        if (unobserved.Unknown.Length != 4) throw new InvalidOperationException("Unobserved and observed empty facts acquired the same identity.");
        var absentProject = Requirement("d", scope: "project");
        var old = GraphReportBuilder.Build(environment, [Node("a", [Mod("d", "1")], [absentProject])]);
        var changed = GraphReportBuilder.Build(environment, [Node("a", [Mod("d", "2")], [absentProject])]);
        if (!old.Issues.Select(issue => issue.Key).Order().SequenceEqual(changed.Issues.Select(issue => issue.Key).Order()))
            throw new InvalidOperationException("Unrelated mod versions entered absent-project modern facts.");
        var runtime = GraphReportBuilder.Build(environment, [], new(true, true));
        if (runtime.Unknown.Length != 2 || runtime.Unknown.Any(issue => issue.Owner != "" || issue.Requirement is not null || issue.WarningRequirement is not null))
            throw new InvalidOperationException("Runtime override ownership changed.");
        try { GraphReportBuilder.Build(environment, [Node("a"), Node("A")]); throw new InvalidOperationException("Duplicate graph node accepted."); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Content.DuplicateIdentity") { }
    }
}
