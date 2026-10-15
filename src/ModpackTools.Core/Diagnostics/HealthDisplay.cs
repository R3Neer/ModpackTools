using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ModpackTools.Core.Graph;

namespace ModpackTools.Core.Diagnostics;

internal static partial class HealthDisplay
{
    public static ImmutableArray<DoctorItem> Read(GraphReport report, bool details, DoctorLimits limits)
    {
        var items = ImmutableArray.CreateBuilder<DoctorItem>(); var characters = 0;
        void Add(string status, string text)
        {
            if (items.Count >= limits.MaximumDisplayItems || text.Length > limits.MaximumDisplayCharacters - characters)
                throw new DomainException("Doctor.DisplayLimit", "Diagnostic details exceed their resource quota", "use bounded diagnostic detail quotas", "LimitsExceeded");
            characters += text.Length; items.Add(new(status, text));
        }
        foreach (var issue in report.Errors) Add("fail", issue.Message);
        long inputCharacters = 0;
        foreach (var issue in report.Warnings.Concat(report.Unknown))
        {
            inputCharacters += (long)issue.Message.Length + issue.Code.Length + issue.Owner.Length + issue.OwnerName.Length;
            if (issue.Requirement is { } requirement) inputCharacters += (long)requirement.Target.Length + requirement.Constraint.Display.Length + requirement.Kind.Length + requirement.Scope.Length + requirement.SuggestedVersionId.Length;
            if (inputCharacters > limits.MaximumDisplayCharacters) throw new DomainException("Doctor.DisplayLimit", "Diagnostic grouping exceeds its character quota", "use bounded diagnostic detail quotas", "LimitsExceeded");
        }
        string Join(IEnumerable<string> values, string separator, int prefixLength)
        {
            var parts = new List<string>(); long length = prefixLength;
            foreach (var value in values)
            {
                length += value.Length + (parts.Count == 0 ? 0 : separator.Length);
                if (length > limits.MaximumDisplayCharacters - characters) throw new DomainException("Doctor.DisplayLimit", "Diagnostic details exceed their resource quota", "use bounded diagnostic detail quotas", "LimitsExceeded");
                parts.Add(value);
            }
            return string.Join(separator, parts);
        }
        var recommendations = Merge(report.Warnings);
        if (recommendations.Length != 0)
        {
            Add("warn", $"{recommendations.Length} optional dependency recommendation(s).");
            foreach (var owner in recommendations.GroupBy(entry => Owner(entry.Issue), StringComparer.CurrentCultureIgnoreCase).OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var texts = owner.OrderBy(entry => entry.Issue.Requirement?.Target ?? "", StringComparer.CurrentCultureIgnoreCase).Select(entry =>
                {
                    if (entry.Issue.Requirement is not { Target.Length: > 0 } requirement) return Text(entry);
                    var range = requirement.Constraint.Display; return requirement.Target + (range.Length != 0 && range != "*" ? " " + range : "") + " [" + Side(entry) + "]";
                });
                Add("info", owner.Key + ": " + Join(texts, "; ", owner.Key.Length + 2));
            }
        }
        var unknown = Merge(report.Unknown);
        if (unknown.Length != 0)
        {
            Add("warn", $"{unknown.Length} requirement(s) could not be verified from the available metadata.");
            foreach (var group in unknown.GroupBy(entry => entry.Issue.Code, StringComparer.CurrentCultureIgnoreCase).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var count = group.Count();
                Add("info", group.Key switch
                {
                    "verification.bundled-selection" => $"{count} bundled mod ID(s) require runtime selection; the loader decides which included version is active.",
                    "environment.java-undeclared" => $"{count} Java requirement(s) cannot be checked because JavaVersion is not declared in .modpack/project.psd1.",
                    "version-range.unsupported" => $"{count} version range(s) could not be interpreted completely.",
                    "provider.version-differs" => $"{count} provider version pointer(s) differ from the installed compatible project version.",
                    "provider.no-compatible-candidate" => $"{count} provider requirement(s) expose no compatible candidate for this project.",
                    "provider.metadata-unavailable" => $"{count} item(s) do not expose provider dependency metadata.",
                    "verification.runtime-override" => $"{count} runtime override file(s) require manual verification.",
                    "verification.metadata" => $"{count} metadata result(s) require manual verification.",
                    _ => $"{count} requirement(s) could not be verified from the available metadata."
                });
                if (details) foreach (var entry in group.OrderBy(Text, StringComparer.CurrentCultureIgnoreCase)) Add("info", "  - " + Text(entry));
            }
            if (!details) Add("info", "Run: modpack doctor --details");
            Add("warn", "Verification is incomplete; Fabric Loader may still reject this pack during startup.");
        }
        else Add("info", "Scope: declared dependency metadata only; game startup, mixins and runtime behaviour are not tested.");
        return items.ToImmutable();
    }
    private sealed record Merged(GraphIssue Issue, HashSet<string> Sides);
    private static ImmutableArray<Merged> Merge(ImmutableArray<GraphIssue> issues)
    {
        // This is the released display grouping boundary, independent of graph
        // identity and resolver contracts. Keep its historical textual grouping.
        var groups = new Dictionary<string, Merged>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var issue in issues)
        {
            var requirement = issue.Requirement;
            var identity = requirement is { Target.Length: > 0 } ? requirement.Target + "|" + requirement.Constraint.Display + "|" + requirement.Kind + "|" + requirement.Scope + "|" + requirement.SuggestedVersionId : SideSuffix().Replace(issue.Message, "");
            var key = issue.Code + "|" + (issue.Code == "verification.bundled-selection" ? "" : issue.Owner) + "|" + identity;
            if (!groups.TryGetValue(key, out var group)) { group = new(issue, new(StringComparer.Ordinal)); groups.Add(key, group); }
            if (issue.Side.Length != 0) group.Sides.Add(issue.Side);
        }
        return [.. groups.Values];
    }
    private static string Owner(GraphIssue issue) => issue.OwnerName.Length != 0 ? issue.OwnerName : issue.Message.IndexOf(':') is var colon && colon >= 0 ? issue.Message[..colon] : issue.Owner;
    private static string Side(Merged merged) => merged.Sides.Contains("client") && merged.Sides.Contains("server") ? "client/server" : merged.Sides.Contains("client") ? "client" : merged.Sides.Contains("server") ? "server" : "both";
    private static string Text(Merged merged)
    {
        var message = SideSuffix().Replace(merged.Issue.Message, ""); var side = Side(merged); return side == "both" ? message : message + " [" + side + "]";
    }
    [GeneratedRegex(@" \[(client|server)\](?=;|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SideSuffix();
}
