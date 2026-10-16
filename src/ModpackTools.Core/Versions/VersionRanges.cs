using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Versions;

public static partial class VersionRanges
{
    [GeneratedRegex("\\s*\\|\\|\\s*", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Alternatives();
    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Whitespace();
    [GeneratedRegex("^(>=|<=|>|<|=|~|\\^)?(.+)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Condition();
    [GeneratedRegex("^(\\d+(?:\\.\\d+)*)\\.(?:x|X|\\*)(?:\\.(?:x|X|\\*))*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Wildcard();
    [GeneratedRegex("^\\d+(?:\\.\\d+)*(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Expansion();
    [GeneratedRegex("([\\[(])([^\\[\\]()]*)([\\])])", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Intervals();

    public static bool? Evaluate(string? version, VersionRange range, VersionScheme scheme = VersionScheme.General,
        VersionRangeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (scheme is not (VersionScheme.General or VersionScheme.Maven)) throw new ArgumentOutOfRangeException(nameof(scheme));
        limits ??= new();
        var scope = new Scope(version ?? "", scheme, limits, ValidatePolicy(limits));
        return scope.Visit(range, 1);
    }

    internal static VersionComparisonLimits ValidatePolicy(VersionRangeLimits limits)
    {
        var invalid = limits.MaximumTextCharacters <= 0 ? nameof(limits.MaximumTextCharacters)
            : limits.MaximumNodes <= 0 ? nameof(limits.MaximumNodes)
            : limits.MaximumComparisons <= 0 ? nameof(limits.MaximumComparisons)
            : limits.MaximumDepth is < 1 or > 64 ? nameof(limits.MaximumDepth) : null;
        if (invalid is not null)
            throw new DomainException("Version.InvalidRangePolicy", "Version-range limits are invalid",
                "use positive range quotas and a depth between 1 and 64", target: invalid);
        return VersionOrdering.Validate(limits.Comparison);
    }

    private sealed class Scope(string version, VersionScheme scheme, VersionRangeLimits limits, VersionComparisonLimits comparison)
    {
        private int nodes;
        private int comparisons;
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private void Guard(bool exceeded, string property)
        {
            if (exceeded) throw new DomainException("Version.RangeLimitExceeded", "Version range exceeds its resource limits",
                "reduce the range input or use explicit quotas within the depth bound", "LimitsExceeded", property,
                details: property + " exceeded.");
        }
        public bool? Visit(VersionRange range, int depth)
        {
            Guard(depth > limits.MaximumDepth, nameof(limits.MaximumDepth));
            Guard(nodes >= limits.MaximumNodes, nameof(limits.MaximumNodes));
            nodes++;
            if (range.Kind == VersionRange.Shape.Unknown) return null;
            if (range.Kind == VersionRange.Shape.Text) return Text(range.Value ?? "", depth);
            var any = range.Kind == VersionRange.Shape.Any;
            var unknown = false;
            foreach (var child in range.Children)
            {
                var result = Visit(child, depth + 1);
                if (result == any) return any;
                if (result is null) unknown = true;
            }
            return unknown ? null : !any;
        }
        private int? Compare(string bound)
        {
            Guard(comparisons >= limits.MaximumComparisons, nameof(limits.MaximumComparisons));
            comparisons++;
            return VersionOrdering.Compare(version, bound, scheme, comparison);
        }
        private bool? Derived(string text, int depth) => Visit(VersionRange.Text(text), depth + 1);
        private bool? Text(string raw, int depth)
        {
            Guard(raw.Length > limits.MaximumTextCharacters, nameof(limits.MaximumTextCharacters));
            var text = raw.Trim();
            if (text.Length == 0 || text == "*") return true;
            if (scheme == VersionScheme.Maven) return Maven(text);
            if (text.Contains("||", StringComparison.Ordinal))
            {
                var unknown = false;
                foreach (var alternative in Alternatives().Split(text))
                {
                    var result = Derived(alternative, depth);
                    if (result == true) return true;
                    if (result is null) unknown = true;
                }
                return unknown ? null : false;
            }
            foreach (var part in Whitespace().Split(text))
            {
                if (part.Length == 0) continue;
                var match = Condition().Match(part);
                if (!match.Success) return null;
                var operation = match.Groups[1].Value;
                var bound = match.Groups[2].Value;
                var wildcard = Wildcard().Match(bound);
                bool? result;
                if (wildcard.Success)
                {
                    if (operation is not ("" or "=")) return null;
                    var lower = wildcard.Groups[1].Value;
                    var components = lower.Split('.');
                    Guard(components[^1].Length > comparison.MaximumNumericDigits, nameof(comparison.MaximumNumericDigits));
                    try { components[^1] = (BigInteger.Parse(components[^1], culture) + 1).ToString(culture); }
                    catch (FormatException exception) { throw BoundFailure(bound, exception); }
                    result = Derived(">=" + lower + " <" + string.Join('.', components), depth);
                }
                else if (operation is "~" or "^")
                {
                    if (!Expansion().IsMatch(bound)) return null;
                    var components = bound.Split('-')[0].Split('.');
                    var numbers = new long[components.Length];
                    try
                    {
                        for (var index = 0; index < components.Length; index++)
                        {
                            Guard(components[index].Length > comparison.MaximumNumericDigits, nameof(comparison.MaximumNumericDigits));
                            numbers[index] = IntegerText.Parse(components[index]);
                        }
                    }
                    catch (Exception exception) when (exception is FormatException or OverflowException) { throw BoundFailure(bound, exception); }
                    var bump = operation == "~" ? Math.Min(1, numbers.Length - 1) : 0;
                    if (operation == "^") while (bump < numbers.Length - 1 && numbers[bump] == 0) bump++;
                    numbers[bump]++;
                    for (var index = bump + 1; index < numbers.Length; index++) numbers[index] = 0;
                    result = Derived(">=" + bound + " <" + string.Join('.', numbers.Select(number => number.ToString(CultureInfo.InvariantCulture))), depth);
                }
                else
                {
                    var order = Compare(bound);
                    if (order is null)
                    {
                        if (operation is not ("" or "=")) return null;
                        result = string.Equals(version, bound, StringComparison.InvariantCulture);
                    }
                    else result = operation switch { ">" => order > 0, ">=" => order >= 0, "<" => order < 0, "<=" => order <= 0, _ => order == 0 };
                }
                if (result is null || result == false) return result;
            }
            return true;
        }
        private bool? Maven(string text)
        {
            if (text[0] is not ('[' or '(')) return true;
            var intervals = Intervals().Matches(text);
            if (intervals.Count == 0 || !string.Equals(Whitespace().Replace(string.Join(',', intervals.Select(interval => interval.Value)), ""),
                Whitespace().Replace(text, ""), StringComparison.InvariantCultureIgnoreCase)) return null;
            foreach (Match interval in intervals)
            {
                var bounds = interval.Groups[2].Value.Split(',');
                if (bounds.Length == 1)
                {
                    if (interval.Groups[1].Value != "[" || interval.Groups[3].Value != "]") return null;
                    if (Compare(bounds[0]) == 0) return true;
                    continue;
                }
                if (bounds.Length != 2) return null;
                var lower = bounds[0].Trim();
                var upper = bounds[1].Trim();
                var low = lower.Length == 0 ? 1 : Compare(lower);
                var high = upper.Length == 0 ? -1 : Compare(upper);
                if ((low > 0 || low == 0 && interval.Groups[1].Value == "[")
                    && (high < 0 || high == 0 && interval.Groups[3].Value == "]")) return true;
            }
            return false;
        }
        private static DomainException BoundFailure(string bound, Exception exception) => new("Version.InvalidRangeBound",
            "Version-range numeric bound could not be evaluated", "check the numeric components in the declared range", "InvalidData", bound,
            details: exception.Message);
    }
}
