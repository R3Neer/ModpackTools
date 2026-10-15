using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Versions;

internal static class RemovalPlanningContracts
{
    public static void Verify()
    {
        var environment = new GraphEnvironment("1.21", "fabric", "0.16", true, "21");
        var target = Node("target"); var owner = Node("owner", [Requirement("target")]);
        try { _ = Plan([target, owner], new(["target"])); throw new Exception("Dependent refusal expected."); }
        catch (DomainException error) when (error.Id == "ModpackTools.Remove.Dependents") { }
        var cascade = Plan([target, owner], new(["target", "target.jar"], Cascade: true));
        Assert(cascade.Nodes.IsEmpty && cascade.Requested.SequenceEqual(["target"]) && cascade.Changes.Select(change => change.Reason).SequenceEqual(["requested", "dependent"]), "cascade order and alias deduplication");
        var taggedOwner = Node("owner", [Requirement("target", "re\u00ADquired")]);
        Assert(Plan([target, taggedOwner], new(["target"], Cascade: true)).Nodes.IsEmpty, "invariant semantic requirement tags match graph validation");
        try { _ = Plan([null!], new(["target"])); throw new Exception("Invalid node expected."); }
        catch (DomainException error) when (error.Id == "ModpackTools.Remove.InvalidState") { }
        try { _ = Plan([target with { Intent = null! }], new(["target"])); throw new Exception("Invalid intent expected."); }
        catch (DomainException error) when (error.Id == "ModpackTools.Remove.InvalidState") { }
        try { _ = Plan([target with { Pinned = true }], new(["target"])); throw new Exception("Requested pin expected."); }
        catch (DomainException error) when (error.Id == "ModpackTools.Compatibility.Pinned") { }
        try { _ = Plan([target, owner], new(["target"], Cascade: true), new(MaximumGraphPasses: 1)); throw new Exception("Pass limit expected."); }
        catch (DomainException error) when (error.Id == "ModpackTools.Remove.PlanningLimitExceeded" && error.Target == new TextTarget("MaximumGraphPasses")) { }
        try { _ = Plan([target], new(["target"]), new(MaximumWork: 1)); throw new Exception("Work limit expected."); }
        catch (DomainException error) when (error.Id == "ModpackTools.Remove.PlanningLimitExceeded") { }
        var dependency = Node("dependency", intent: "transitive"); var unrelated = Node("unrelated", intent: "transitive");
        var removedRoot = Node("root", [Requirement("dependency", "optional")]);
        var cleanup = Plan([removedRoot, dependency, unrelated], new(["root"], AutoRemove: true));
        Assert(cleanup.Changes.Select(change => change.Id).SequenceEqual(["root", "dependency"]) && cleanup.Nodes.Single().Graph.Id == "unrelated", "only affected unreachable managed transitive dependency removed");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { _ = RemovalPlanning.Prepare(environment, [target], new(["target"]), cancellation: cancelled.Token); throw new Exception("Cancellation expected."); }
        catch (DomainException error) when (error.Id == "ModpackTools.Core.OperationCancelled") { }
        RemovalPlan Plan(ImmutableArray<ProjectContentNode> nodes, RemovalPlanRequest request, RemovalPlanningLimits? limits = null) => RemovalPlanning.Prepare(environment, nodes, request, limits: limits);
    }
    private static ProjectContentNode Node(string id, ImmutableArray<ManifestRequirement> requirements = default, string intent = "explicit")
    {
        var item = new InventoryItem(id, ContentKind.Mod, id, id + ".jar", "both", ContentSource.Packwiz, "v1", id + ".pw.toml", false);
        return new(item, new(id, "v1", id, "mod", "both", true, [], requirements.IsDefault ? [] : requirements, []), null, null, false, intent);
    }
    private static ManifestRequirement Requirement(string target, string kind = "required") => new(target, VersionConstraint.Text("*"), kind, "both", "project", VersionScheme.General, "provider", "", [], [], null);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Removal planning contract: " + message); }
}
