using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Versions;

internal static class RequirementContracts
{
    public static void Verify()
    {
        var context = RequirementContext.Create([new("p", "Version")], [new("m", [null, "1"]), new("M", ["2"]), new("empty", [])]);
        ManifestRequirement R(string target, string kind = "required", VersionRange? range = null) => new(target, range ?? VersionRange.Text("*"),
            "", kind, "both", "mod", VersionScheme.General, "manifest", "", [], [], null);
        var known = R("m"); var unknown = R("java"); var absent = R("missing");
        if (RequirementEvaluator.Evaluate(known, context) != true || RequirementEvaluator.Evaluate(unknown, context) is not null
            || RequirementEvaluator.Evaluate(R("missing", "recommended"), context) != false
            || RequirementEvaluator.Evaluate(R("missing", "optional"), context) != true
            || RequirementEvaluator.Evaluate(R("empty"), context) != false) throw new InvalidOperationException("Requirement presence semantics changed.");
        var bad = R("m", range: VersionRange.Text("~٢.0"));
        if (RequirementEvaluator.Evaluate(bad with { Unless = known }, context) != true
            || RequirementEvaluator.Evaluate(bad with { Unless = unknown }, context) is not null
            || RequirementEvaluator.Evaluate(absent with { Any = [known, bad], All = [unknown] }, context) != true
            || RequirementEvaluator.Evaluate(known with { All = [absent, bad] }, context) != false) throw new InvalidOperationException("Requirement short circuit priorities changed.");
        var project = R("p") with { Scope = "project", Range = VersionRange.Text("Version"), SuggestedVersionId = "other" };
        if (RequirementEvaluator.Evaluate(project, context) is not null
            || RequirementEvaluator.Evaluate(project with { Range = VersionRange.Text(null), SuggestedVersionId = "" }, context) != false)
            throw new InvalidOperationException("Project constraint or suggestion semantics changed.");
        try { RequirementEvaluator.Evaluate(known, context, new(MaximumVersionChecks: 1)); throw new InvalidOperationException("Empty version bypassed its check quota."); }
        catch (DomainException exception) when (exception.Id.EndsWith("Compatibility.RequirementLimitExceeded", StringComparison.Ordinal)) { }
        try { RequirementContext.Create([], [new("", []), new("", [])], new(MaximumRows: 1)); throw new InvalidOperationException("Empty duplicate context rows bypassed ownership quota."); }
        catch (DomainException exception) when (exception.Id.EndsWith("Compatibility.ContextLimitExceeded", StringComparison.Ordinal)) { }
    }
}
