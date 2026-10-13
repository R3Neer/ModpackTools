using ModpackTools.Core;
using ModpackTools.Core.Versions;

internal static class VersionOrderingContracts
{
    public static void Verify()
    {
        foreach (var scheme in new[] { VersionScheme.General, VersionScheme.Maven })
        {
            if (VersionOrdering.Compare(null, "", scheme) != 0 || VersionOrdering.Compare("é", "e\u0301", scheme) != 0
                || VersionOrdering.Compare("ab", "a\u00adb", scheme) != 0 || VersionOrdering.Compare("١", "1", scheme) != 0)
                throw new InvalidOperationException("Version equality lost legacy binding or invariant cultural aliases.");
        }
        if (VersionOrdering.Compare("1-a", "1-z") != -25 || VersionOrdering.Compare("1-a", "1-A") != 32
            || VersionOrdering.Compare("unsupported", "different") is not null
            || VersionOrdering.Compare("2.١", "1.0") != 1
            || VersionOrdering.Compare("2.123456", "1.0", limits: new(MaximumNumericDigits: 1)) != 1)
            throw new InvalidOperationException("General version ordering lost raw comparisons, unknowns or deferred components.");
        Expect(() => VersionOrdering.Compare("1.١", "1.0"), "Version.InvalidNumericComparison", "Left");
        Expect(() => VersionOrdering.Compare("2.١", "1.0", VersionScheme.Maven), "Version.InvalidNumericComparison", "Left");
        Expect(() => VersionOrdering.Compare("1.0", "2.١", VersionScheme.Maven), "Version.InvalidNumericComparison", "Right");
        if (VersionOrdering.Compare("1-", "1", VersionScheme.Maven) != 0
            || VersionOrdering.Compare("1-", "1") != -1
            || VersionOrdering.Compare("1\n", "1") != 0 || VersionOrdering.Compare("1\n", "1", VersionScheme.Maven) != 2)
            throw new InvalidOperationException("General and Maven syntax were incorrectly unified.");
        var bounded = new VersionComparisonLimits(MaximumTextCharacters: 4, MaximumNumericDigits: 1, MaximumItems: 5, MaximumDepth: 2);
        _ = VersionOrdering.Compare("1-a1", "1.0", VersionScheme.Maven, bounded);
        Expect(() => VersionOrdering.Compare("1-a1", "1.0", VersionScheme.Maven, bounded with { MaximumItems = 4 }), "Version.ComparisonLimitExceeded", "Left");
        Expect(() => VersionOrdering.Compare("1-a1", "1.0", VersionScheme.Maven, bounded with { MaximumDepth = 1 }), "Version.ComparisonLimitExceeded", "Left");
        Expect(() => VersionOrdering.Compare("same", "same", limits: new(MaximumTextCharacters: 3)), "Version.ComparisonLimitExceeded", "Left");
        Expect(() => VersionOrdering.Compare("same", "same", limits: new(MaximumDepth: 65)), "Version.InvalidComparisonPolicy", "MaximumDepth");
        Expect(() => VersionOrdering.Compare("12", "1", limits: new(MaximumNumericDigits: 1)), "Version.ComparisonLimitExceeded", "Left");
    }
    private static void Expect(Func<int?> action, string id, string target)
    {
        try { action(); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id && exception.Target is TextTarget text && text.Value == target) { return; }
        throw new InvalidOperationException("Expected version ordering error " + id + " for " + target);
    }
}
