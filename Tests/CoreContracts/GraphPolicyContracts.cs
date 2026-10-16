using ModpackTools.Core;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

internal static class GraphPolicyContracts
{
    public static void Verify()
    {
        var environment = new GraphEnvironment("1.21", "fabric", "0.16");
        ManifestRequirement Missing(string target) => new(target, VersionConstraint.Text("*"), "required", "both",
            "mod", VersionScheme.General, "manifest", "", [], [], null);
        var error = GraphReportBuilder.Build(environment, [new("a", "v1", "A", "resourcepack", "both", true, [], [Missing("x")], [])]);
        var mixed = GraphReportBuilder.Build(environment, [new("a", "v1", "A", "resourcepack", "both", true, [], [Missing("x")], ["unknown fixture"])]);
        var empty = GraphReportBuilder.Build(environment, []);
        var unknown = GraphReportBuilder.Build(environment, [], new(true));
        var original = mixed.Issues.ToArray();
        foreach (var strict in new[] { false, true }) foreach (var build in new[] { false, true })
        {
            GraphPolicy.Assert(empty, mixed, strict, build);
            Check(error, null, strict, build, true);
            Check(error, empty, strict, build, true);
            Check(error, error, strict, build, strict || build);
            Check(unknown, null, strict, build, strict);
            Check(mixed, error, strict, build, strict || build);
        }
        if (!mixed.Issues.SequenceEqual(original) || mixed.Errors.Length != 2 || mixed.Unknown.Length != 1)
            throw new InvalidOperationException("Graph policy mutated its report.");
        var details = string.Join("; ", mixed.Errors.Concat(mixed.Unknown).Select(issue => issue.Message));
        try { GraphPolicy.Assert(mixed, strict: true, limits: new(details.Length)); throw new InvalidOperationException("Strict policy allowed mixed findings."); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Compatibility.GraphBlocked")
        { if (exception.Details != details || exception.Target is not null || exception.Category != "InvalidOperation") throw new InvalidOperationException("Graph policy detail order changed."); }
        try { GraphPolicy.Assert(mixed, strict: true, limits: new(details.Length - 1)); throw new InvalidOperationException("Policy detail quota not enforced."); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Compatibility.PolicyDetailLimit") { }
        try { GraphPolicy.Assert(empty, limits: new(0)); throw new InvalidOperationException("Invalid quota allowed healthy graph."); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Compatibility.InvalidGraphPolicy") { }
        void Check(GraphReport report, GraphReport? baseline, bool strict, bool build, bool blocked)
        {
            try { GraphPolicy.Assert(report, baseline, strict, build); if (blocked) throw new InvalidOperationException("Blocked graph was allowed."); }
            catch (DomainException exception) when (exception.Id == "ModpackTools.Compatibility.GraphBlocked")
            { if (!blocked) throw new InvalidOperationException("Allowed graph was blocked."); }
        }
    }
}
