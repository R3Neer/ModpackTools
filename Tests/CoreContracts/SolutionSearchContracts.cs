using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Resolution;

internal static class SolutionSearchContracts
{
    public static async Task VerifyAsync()
    {
        var environment = new GraphEnvironment("1.21.1", "fabric", "0.16");
        var requirement = new ManifestRequirement("modrinth:dep", VersionConstraint.Text("d2"), "required", "both", "project",
            ModpackTools.Core.Versions.VersionScheme.General, "manifest", "", [], [], null);
        var owner = Node("modrinth:owner", "o1", [requirement]);
        var old = Node("modrinth:dep", "d0");
        var input = new ResolutionInput(environment, [owner, old], [owner, old], [owner.Graph.Id], []);
        var source = new Source([Node("modrinth:dep", "d1"), old, Node("modrinth:dep", "d2")]);
        var result = await DependencyResolver.SolveAsync(input, source);
        if (result.Solution?.Nodes.Last().Graph.VersionId != "d2" || result.VisitedStates != 3 || result.Solution.Cost.Changed != 1 || result.LastReport is null)
            throw new Exception("Backtracking/cycle/cost/last failed report changed.");
        _ = await DependencyResolver.SolveAsync(input, new Source(source.Nodes), new(MaximumStates: 3));
        try { _ = await DependencyResolver.SolveAsync(input, new Source(source.Nodes), new(MaximumStates: 2)); throw new Exception("State limit not enforced."); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Compatibility.SearchLimit") { }
        var pinned = old with { Pinned = true }; var blocked = new Source(source.Nodes);
        result = await DependencyResolver.SolveAsync(input with { Before = [owner, pinned] }, blocked);
        if (result.Solution is not null || blocked.Calls != 0) throw new Exception("Pinned state was expanded.");
        var tolerated = GraphReportBuilder.Build(environment, [owner.Graph]);
        result = await DependencyResolver.SolveAsync(input with { Initial = [owner], BaselineKeys = [.. tolerated.Errors.Select(issue => issue.Key)], PreviousReport = tolerated }, new Source([]));
        if (result.Solution is null || result.Solution.Report.Errors.IsEmpty || result.LastReport != tolerated) throw new Exception("Baseline errors or prior report lost.");
        var older = Node("modrinth:dep", "old") with { OrderingDate = new DateTime(2025, 1, 1), Graph = old.Graph with { Mods = [Mod("dep", "1")] } };
        var newer = old with { OrderingDate = new DateTime(2026, 1, 1), Graph = old.Graph with { Mods = [Mod("dep", "2")] } };
        if (!DependencyResolver.IsDowngrade(newer, older)) throw new Exception("Native mod downgrade was missed.");
        if (DependencyResolver.IsDowngrade(newer, older with { Graph = newer.Graph })) throw new Exception("Comparable equal mod version fell back to publication.");
        var unicode = await DependencyResolver.SolveAsync(input, new Source([Node("modrinth:dep", "\ud800"), Node("modrinth:dep", "\ud801"), Node("modrinth:dep", "d2")]));
        if (unicode.Solution is null || unicode.VisitedStates != 4) throw new Exception("Distinct UTF16 version states collided.");
        var cultureBefore = Node("modrinth:dep", "foo");
        var cultureAfter = Node("modrinth:dep", "fo\u00ado");
        var culture = await DependencyResolver.SolveAsync(new(environment, [cultureBefore], [cultureAfter], [], []), new Source([]));
        if (culture.Solution?.Cost.Changed != 0) throw new Exception("Case-sensitive invariant version cost changed.");
        var mavenBefore = newer with { Maven = true, Graph = newer.Graph with { Mods = [Mod("dep", "1.0")] } };
        var mavenAfter = older with { Graph = older.Graph with { Mods = [Mod("dep", "1.0-alpha")] } };
        if (!DependencyResolver.IsDowngrade(mavenBefore, mavenAfter)) throw new Exception("Before Maven scheme was lost.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { _ = await DependencyResolver.SolveAsync(input, new Source([]), cancellation: cancellation.Token); throw new Exception("Cancelled search ran."); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Core.OperationCancelled") { }
    }
    private static ResolutionNode Node(string id, string version, ImmutableArray<ManifestRequirement> requirements = default) =>
        new(new(id, version, id, "mod", "both", true, [], requirements.IsDefault ? [] : requirements, []));
    private static ArtifactMod Mod(string id, string version) => new(new(id, version, "both", [], []), false, []);
    private sealed class Source(ImmutableArray<ResolutionNode> nodes) : IResolutionCandidates
    {
        public ImmutableArray<ResolutionNode> Nodes { get; } = nodes;
        public int Calls { get; private set; }
        public ValueTask<ResolutionDomain> GetCandidatesAsync(string id, CancellationToken cancellation)
        { Calls++; return ValueTask.FromResult(new ResolutionDomain(Nodes.IsEmpty ? CandidateAvailability.KnownEmpty : CandidateAvailability.KnownAvailable, [.. Nodes.Select(value => value.Graph.VersionId)])); }
        public ValueTask<ResolutionNode> GetNodeAsync(string id, string versionId, CancellationToken cancellation)
        { Calls++; return ValueTask.FromResult(Nodes.First(value => value.Graph.VersionId == versionId)); }
    }
}
