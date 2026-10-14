using System.Collections.Immutable;
using ModpackTools.Core.Artifacts;

namespace ModpackTools.Core.Graph;

public sealed record GraphEnvironment(string MinecraftVersion, string Loader, string LoaderVersion,
    bool JavaDeclared = false, string JavaVersion = "");
public sealed record RuntimeOverrides(bool FabricDependencies = false, bool QuiltOverrides = false);
public sealed record GraphNode(string Id, string VersionId, string Name, string Kind, string Side, bool Enabled,
    ImmutableArray<ArtifactMod> Mods, ImmutableArray<ManifestRequirement> Requirements, ImmutableArray<string> Warnings);
public sealed record GraphReportLimits(int MaximumNodes = 100000, int MaximumMods = 100000,
    int MaximumAliases = 100000, int MaximumRequirements = 100000, int MaximumWarnings = 100000,
    int MaximumIssues = 100000, int MaximumFacts = 1000000, int MaximumFactVersions = 1000000, int MaximumTextCharacters = 32 * 1024 * 1024,
    int MaximumRequirementDepth = 64, int MaximumEvaluatedNodes = 1000000, int MaximumEvaluatedVersions = 1000000,
    int MaximumIdentityCharacters = 64 * 1024 * 1024, int MaximumMessageCharacters = 64 * 1024 * 1024,
    RequirementContextLimits? Context = null, RequirementEvaluationLimits? Evaluation = null);

public sealed class GraphIssue
{
    public string Key { get; }
    public string Code { get; }
    public string Owner { get; }
    public string OwnerName { get; }
    public ManifestRequirement? Requirement { get; }
    public string? WarningRequirement { get; }
    public string Side { get; }
    public string Severity { get; }
    public string Message { get; }
    internal string? AuxiliarySortKey { get; }
    internal GraphIssue(string key, string? auxiliary, string code, GraphNode? owner, ManifestRequirement? requirement,
        string? warning, string side, string severity, string message)
    {
        Key = key; AuxiliarySortKey = auxiliary; Code = code; Owner = owner?.Id ?? ""; OwnerName = owner?.Name ?? "";
        Requirement = requirement; WarningRequirement = warning; Side = side; Severity = severity; Message = message;
    }
}
public sealed class GraphReport
{
    public const int ValidatorRevision = 4;
    public const int IssueKeySchema = 2;
    public ImmutableArray<GraphIssue> Issues { get; }
    public ImmutableArray<GraphIssue> Errors { get; }
    public ImmutableArray<GraphIssue> Unknown { get; }
    public ImmutableArray<GraphIssue> Warnings { get; }
    public bool Complete => Unknown.IsEmpty;
    internal GraphReport(ImmutableArray<GraphIssue> issues)
    {
        Issues = issues; Errors = [.. issues.Where(issue => issue.Severity == "error")];
        Unknown = [.. issues.Where(issue => issue.Severity == "unknown")];
        Warnings = [.. issues.Where(issue => issue.Severity == "warning")];
    }
}

internal sealed class RequirementEvaluationBudget(int maximumNodes, int maximumVersions)
{
    private int nodes, versions;
    public void Node() { GraphResource.Guard(nodes >= maximumNodes, "MaximumEvaluatedNodes"); nodes++; }
    public void Version() { GraphResource.Guard(versions >= maximumVersions, "MaximumEvaluatedVersions"); versions++; }
}
internal static class GraphResource
{
    public static void Guard(bool exceeded, string property)
    {
        if (exceeded) throw new DomainException("Compatibility.GraphLimitExceeded", "Dependency graph exceeds its resource limits",
            "reduce graph inputs or use explicit bounded quotas", "LimitsExceeded", property, details: property + " exceeded.");
    }
}
