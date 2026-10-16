using System.Text;

namespace ModpackTools.Core.Graph;

public sealed record GraphPolicyLimits(int MaximumDetailCharacters = 1024 * 1024);

public static class GraphPolicy
{
    public static void Assert(GraphReport report, GraphReport? baseline = null, bool strict = false,
        bool build = false, GraphPolicyLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        limits ??= new();
        if (limits.MaximumDetailCharacters <= 0)
            throw new DomainException("Compatibility.InvalidGraphPolicy", "Graph policy limits must be positive",
                "use a positive detail-character quota", target: nameof(limits.MaximumDetailCharacters));
        var acceptedKeys = baseline is not null && !strict && !build
            ? baseline.Errors.Select(issue => issue.Key).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        long characters = 0;
        var count = 0;
        foreach (var issue in Selected())
        {
            characters += issue.Message.Length + (count == 0 ? 0L : 2L);
            if (characters > limits.MaximumDetailCharacters)
                throw new DomainException("Compatibility.PolicyDetailLimit", "Graph validation details exceed their resource limit",
                    "inspect the report separately or use an explicit bounded detail quota", "LimitsExceeded",
                    nameof(limits.MaximumDetailCharacters));
            count++;
        }
        if (count == 0) return;
        var details = new StringBuilder((int)characters);
        var first = true;
        foreach (var issue in Selected())
        {
            if (!first) details.Append("; ");
            first = false;
            details.Append(issue.Message);
        }
        throw new DomainException("Compatibility.GraphBlocked", "The resulting pack does not satisfy dependency validation",
            "resolve the listed requirements, unpin a blocking version, or inspect incomplete metadata",
            "InvalidOperation", details: details.ToString());
        IEnumerable<GraphIssue> Selected()
        {
            foreach (var issue in report.Errors)
                if (acceptedKeys is null || !acceptedKeys.Contains(issue.Key)) yield return issue;
            if (strict) foreach (var issue in report.Unknown) yield return issue;
        }
    }
}
