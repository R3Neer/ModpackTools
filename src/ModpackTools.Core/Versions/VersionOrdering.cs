using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ModpackTools.Core.Versions;

public enum VersionScheme { General, Maven }
public sealed record VersionComparisonLimits(int MaximumTextCharacters = 16 * 1024,
    int MaximumNumericDigits = 4096, int MaximumItems = 4096, int MaximumDepth = 64);

public static partial class VersionOrdering
{
    [GeneratedRegex("^(\\d+(?:\\.\\d+)*)(?:-([0-9A-Za-z.-]*))?(?:\\+.*)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Syntax();

    public static int? Compare(string? left, string? right, VersionScheme scheme = VersionScheme.General,
        VersionComparisonLimits? limits = null)
    {
        if (scheme is not (VersionScheme.General or VersionScheme.Maven)) throw new ArgumentOutOfRangeException(nameof(scheme));
        limits ??= new();
        var invalid = limits.MaximumTextCharacters <= 0 ? nameof(limits.MaximumTextCharacters)
            : limits.MaximumNumericDigits <= 0 ? nameof(limits.MaximumNumericDigits)
            : limits.MaximumItems <= 0 ? nameof(limits.MaximumItems)
            : limits.MaximumDepth is < 1 or > 64 ? nameof(limits.MaximumDepth) : null;
        if (invalid is not null)
            throw new DomainException("Version.InvalidComparisonPolicy", "Version-comparison limits are invalid",
                "use positive comparison quotas and a Maven depth between 1 and 64", target: invalid);
        left ??= "";
        right ??= "";
        var culture = CultureInfo.CurrentCulture;
        var aScope = new VersionScope("Left", limits, culture);
        var bScope = new VersionScope("Right", limits, culture);
        aScope.Text(left);
        bScope.Text(right);
        if (string.Equals(left, right, StringComparison.InvariantCulture)) return 0;
        if (scheme == VersionScheme.Maven) return MavenOrdering.Compare(left, right, aScope, bScope);
        var a = Syntax().Match(left);
        var b = Syntax().Match(right);
        if (!a.Success || !b.Success) return null;
        var aParts = new Parts(a.Groups[1].ValueSpan, a.Groups[1].Index);
        var bParts = new Parts(b.Groups[1].ValueSpan, b.Groups[1].Index);
        for (var index = 0; index < Math.Max(aParts.Count, bParts.Count); index++)
        {
            var aValue = aParts.Take(out var aPart, out var aOffset) ? aScope.Number(aPart, aOffset) : BigInteger.Zero;
            var bValue = bParts.Take(out var bPart, out var bOffset) ? bScope.Number(bPart, bOffset) : BigInteger.Zero;
            var result = aValue.CompareTo(bValue);
            if (result != 0) return result;
        }
        if (!a.Groups[2].Success && !b.Groups[2].Success) return 0;
        if (!a.Groups[2].Success) return 1;
        if (!b.Groups[2].Success) return -1;
        aParts = new(a.Groups[2].ValueSpan, a.Groups[2].Index);
        bParts = new(b.Groups[2].ValueSpan, b.Groups[2].Index);
        for (var index = 0; index < Math.Min(aParts.Count, bParts.Count); index++)
        {
            aParts.Take(out var aPart, out var aOffset);
            bParts.Take(out var bPart, out var bOffset);
            var aNumeric = Digits(aPart);
            var bNumeric = Digits(bPart);
            var result = aNumeric && bNumeric ? aScope.Number(aPart, aOffset).CompareTo(bScope.Number(bPart, bOffset))
                : aNumeric != bNumeric ? aNumeric ? -1 : 1
                // Keep String.CompareOrdinal's full raw result; span comparison
                // may give a different magnitude at a string terminator.
                : string.CompareOrdinal(aPart.ToString(), bPart.ToString());
            if (result != 0) return result;
        }
        return aParts.Count.CompareTo(bParts.Count);
    }

    private static bool Digits(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return false;
        foreach (var character in text) if (!char.IsDigit(character)) return false;
        return true;
    }

    private ref struct Parts
    {
        private ReadOnlySpan<char> remaining;
        private int offset;
        private bool ended;
        public int Count { get; }
        public Parts(ReadOnlySpan<char> text, int start)
        {
            remaining = text;
            offset = start;
            ended = false;
            var count = 1;
            foreach (var character in text) if (character == '.') count++;
            Count = count;
        }
        public bool Take(out ReadOnlySpan<char> part, out int start)
        {
            start = offset;
            if (ended) { part = default; return false; }
            var next = remaining.IndexOf('.');
            if (next < 0) { part = remaining; ended = true; }
            else { part = remaining[..next]; remaining = remaining[(next + 1)..]; offset += next + 1; }
            return true;
        }
    }
}

internal sealed class VersionScope(string argument, VersionComparisonLimits limits, CultureInfo culture)
{
    private int items;
    public void Text(string text)
    {
        if (text.Length > limits.MaximumTextCharacters) Limit(nameof(limits.MaximumTextCharacters));
    }
    public BigInteger Number(ReadOnlySpan<char> text, int offset)
    {
        if (text.Length > limits.MaximumNumericDigits) Limit(nameof(limits.MaximumNumericDigits), offset);
        try { return BigInteger.Parse(text, NumberStyles.Integer, culture); }
        catch (FormatException exception)
        {
            throw new DomainException("Version.InvalidNumericComparison", "Version numeric component could not be compared",
                "check the declared version text and comparison scheme", "InvalidData", argument,
                details: "Numeric component at offset " + offset.ToString(CultureInfo.InvariantCulture) + ": " + exception.Message);
        }
    }
    public void Item()
    {
        if (items >= limits.MaximumItems) Limit(nameof(limits.MaximumItems));
        items++;
    }
    public void Depth(int depth)
    {
        if (depth > limits.MaximumDepth) Limit(nameof(limits.MaximumDepth));
    }
    private void Limit(string property, int? offset = null) => throw new DomainException("Version.ComparisonLimitExceeded",
        "Version comparison exceeds its resource limits", "reduce the version input or use explicit comparison quotas within the depth bound",
        "LimitsExceeded", argument, details: property + " exceeded" + (offset is { } value ? " at offset " + value.ToString(CultureInfo.InvariantCulture) : "") + ".");
}
