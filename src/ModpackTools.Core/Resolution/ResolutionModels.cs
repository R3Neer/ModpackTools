using System.Collections.Immutable;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Providers;

namespace ModpackTools.Core.Resolution;

public sealed record ResolutionNode(GraphNode Graph, bool Pinned = false, bool Local = false,
    bool Explicit = false, DateTime? OrderingDate = null, bool Maven = false);
public sealed record ResolutionDomain(CandidateAvailability Availability, ImmutableArray<string> VersionIds);

// An invocation owns this source. A pair must yield the same immutable node throughout
// that invocation. Candidate order is the provider's publication/ID order; acquisition
// of an archive is deferred until GetNodeAsync, never performed for an entire domain.
public interface IResolutionCandidates
{
    ValueTask<ResolutionDomain> GetCandidatesAsync(string id, CancellationToken cancellation);
    ValueTask<ResolutionNode> GetNodeAsync(string id, string versionId, CancellationToken cancellation);
}

public sealed record ResolutionInput(GraphEnvironment Environment, ImmutableArray<ResolutionNode> Before,
    ImmutableArray<ResolutionNode> Initial, ImmutableArray<string> Roots, ImmutableArray<string> BaselineKeys,
    bool Strict = false, bool AllowDowngrade = false, RuntimeOverrides? RuntimeOverrides = null, GraphReport? PreviousReport = null);
public sealed record ResolutionLimits(int MaximumStates = 10000, int MaximumDomains = 10000,
    int MaximumCandidateVersions = 100000, int MaximumCandidateNodes = 10000,
    long MaximumWork = 10000000, long MaximumIdentityBytes = 64L * 1024 * 1024,
    long MaximumRetainedCharacters = 64L * 1024 * 1024, GraphReportLimits? Graph = null);
public readonly record struct ResolutionCost(int Changed, int ExplicitChanged, int Added, decimal NegativeFreshness)
    : IComparable<ResolutionCost>
{
    public int CompareTo(ResolutionCost other)
    {
        var value = Changed.CompareTo(other.Changed); if (value != 0) return value;
        value = ExplicitChanged.CompareTo(other.ExplicitChanged); if (value != 0) return value;
        value = Added.CompareTo(other.Added); return value != 0 ? value : NegativeFreshness.CompareTo(other.NegativeFreshness);
    }
}
public sealed record DependencySolution(ImmutableArray<ResolutionNode> Nodes, GraphReport Report,
    ResolutionCost Cost, int VisitedStates);
public sealed record DependencySearchResult(DependencySolution? Solution, GraphReport? LastReport, int VisitedStates);
