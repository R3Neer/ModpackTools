using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Graph;

public sealed record RequirementEvaluationLimits(int MaximumNodes = 100000, int MaximumVersionChecks = 100000,
    int MaximumDepth = 64, VersionRangeLimits? Ranges = null);

public static class RequirementEvaluator
{
    public static bool? Evaluate(ManifestRequirement requirement, RequirementContext context, RequirementEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(requirement); ArgumentNullException.ThrowIfNull(context);
        limits ??= new();
        ValidatePolicy(limits);
        return EvaluateValidated(requirement, context, limits, null);
    }

    internal static void ValidatePolicy(RequirementEvaluationLimits limits)
    {
        var invalid = limits.MaximumNodes <= 0 ? nameof(limits.MaximumNodes)
            : limits.MaximumVersionChecks <= 0 ? nameof(limits.MaximumVersionChecks)
            : limits.MaximumDepth is < 1 or > 64 ? nameof(limits.MaximumDepth) : null;
        if (invalid is not null) throw new DomainException("Compatibility.InvalidRequirementPolicy", "Requirement evaluation limits are invalid",
            "use positive quotas and a depth between 1 and 64", target: invalid);
        VersionRanges.ValidatePolicy(limits.Ranges ?? new());
    }

    internal static bool? EvaluateValidated(ManifestRequirement requirement, RequirementContext context,
        RequirementEvaluationLimits limits, RequirementEvaluationBudget? budget)
        => new Scope(context, limits, budget).Visit(requirement, 1);

    private static bool Is(string? value, string expected) => StringComparer.InvariantCultureIgnoreCase.Equals(value, expected);
    private static bool Negative(string? kind) => Is(kind, "incompatible") || Is(kind, "discouraged");
    private sealed class Scope(RequirementContext context, RequirementEvaluationLimits limits, RequirementEvaluationBudget? budget)
    {
        private int nodes, checks;
        private static void Guard(bool exceeded, string property)
        {
            if (exceeded) throw new DomainException("Compatibility.RequirementLimitExceeded", "Requirement evaluation exceeds its resource limits",
                "reduce the requirement input or use explicit quotas within the depth bound", "LimitsExceeded", property,
                details: property + " exceeded.");
        }
        public bool? Visit(ManifestRequirement requirement, int depth)
        {
            ArgumentNullException.ThrowIfNull(requirement);
            Guard(depth > limits.MaximumDepth, nameof(limits.MaximumDepth));
            Guard(nodes >= limits.MaximumNodes, nameof(limits.MaximumNodes)); budget?.Node(); nodes++;
            if (requirement.Unless is not null)
            {
                var unless = Visit(requirement.Unless, depth + 1);
                if (unless != false) return unless;
            }
            var any = !requirement.Any.IsDefaultOrEmpty;
            var children = any ? requirement.Any : requirement.All;
            var negative = Negative(requirement.Kind);
            if (!children.IsDefaultOrEmpty)
            {
                var unknown = false;
                foreach (var child in children)
                {
                    var result = Visit(child, depth + 1);
                    if (any && result == true) return !negative;
                    if (!any && result == false) return negative;
                    if (result is null) unknown = true;
                }
                return unknown ? null : negative ? any : !any;
            }
            if (string.IsNullOrEmpty(requirement.Target)) return null;
            var project = Is(requirement.Scope, "project");
            var present = project ? context.Projects.ContainsKey(requirement.Target) : context.Mods.ContainsKey(requirement.Target);
            if (!present)
            {
                if (Is(requirement.Target, "java") || Is(requirement.Target, "javafml") || Is(requirement.Target, "lowcodefml")) return null;
                return Is(requirement.Kind, "optional") || negative;
            }
            var matches = false; var uncertain = false;
            if (project)
            {
                // Public provider constraints are text. Unknown typed shapes cannot
                // recover arbitrary old boxed-value casts and never imply health.
                if (requirement.Range.Kind != VersionRange.Shape.Text) return null;
                var version = context.Projects[requirement.Target];
                matches = Is(requirement.Range.Value, "*") || requirement.Range.Value is not null
                    && StringComparer.InvariantCulture.Equals(version, requirement.Range.Value);
                if (matches && !string.IsNullOrEmpty(requirement.SuggestedVersionId)
                    && !StringComparer.InvariantCulture.Equals(version, requirement.SuggestedVersionId)) return null;
            }
            else
                foreach (var version in context.Mods[requirement.Target])
                {
                    Guard(checks >= limits.MaximumVersionChecks, nameof(limits.MaximumVersionChecks)); budget?.Version(); checks++;
                    if (string.IsNullOrEmpty(version)) { uncertain = true; continue; }
                    var result = VersionRanges.Evaluate(version, requirement.Range, requirement.Scheme, limits.Ranges);
                    if (result == true) { matches = true; break; }
                    if (result is null) uncertain = true;
                }
            if (!matches && uncertain) return null;
            return negative ? !matches : matches;
        }
    }
}
