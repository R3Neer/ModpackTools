using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Versions;

internal static class VersionRangeContracts
{
    public static void Verify()
    {
        static VersionRange Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return VersionRangeDeclaration.Read(document.RootElement);
        }
        if (VersionRanges.Evaluate("1", VersionRange.Any([])) != false
            || VersionRanges.Evaluate("1", VersionRange.All([])) != true
            || VersionRanges.Evaluate("1", VersionRange.Any([VersionRange.Unknown, VersionRange.Text("1")])) != true
            || VersionRanges.Evaluate("1", VersionRange.All([VersionRange.Unknown, VersionRange.Text("2")])) != false
            || VersionRanges.Evaluate("1", VersionRange.Text(">opaque <0")) is not null
            || VersionRanges.Evaluate("1", Parse("{\"any\":[[\"1\",\"2\"]]}")) != true
            || VersionRanges.Evaluate("1", Parse("{\"all\":[[\"1\",\"2\"]]}")) != false
            || VersionRanges.Evaluate("1", Parse("{\"any\":[[]],\"all\":\"*\"}")) != true
            || VersionRanges.Evaluate("1", Parse("{\"any\":[false,false],\"all\":\"*\"}")) is not null)
            throw new InvalidOperationException("Typed range logic or member-lowering semantics changed.");
        if (VersionRanges.Evaluate("1", VersionRange.Text("[1],(bad)"), VersionScheme.Maven) != true
            || VersionRanges.Evaluate("1", VersionRange.Text("[1],(1,2,3]"), VersionScheme.Maven) != true
            || VersionRanges.Evaluate("2147483647", VersionRange.Text("^2147483647")) != true
            || VersionRanges.Evaluate("1.2147483647", VersionRange.Text("~1.2147483647")) != true)
            throw new InvalidOperationException("Range interval priorities or Int32 increment promotion changed.");
        Expect(() => VersionRanges.Evaluate("1", VersionRange.Text("~2147483648")), "Version.InvalidRangeBound");
        _ = VersionRanges.Evaluate("1", VersionRange.Text("1 || ~2147483648"));
        _ = VersionRanges.Evaluate("1", VersionRange.Text("*"), limits: new(MaximumNodes: 1, MaximumDepth: 1));
        Expect(() => VersionRanges.Evaluate("1", VersionRange.Any([VersionRange.Text("*")]), limits: new(MaximumNodes: 1)), "Version.RangeLimitExceeded");
        Expect(() => VersionRanges.Evaluate("1", VersionRange.Text("*"), limits: new(MaximumDepth: 65)), "Version.InvalidRangePolicy");
        _ = VersionRanges.Evaluate(new string('a', 20), VersionRange.Text("*"), limits: new(Comparison: new(MaximumTextCharacters: 1)));
        using var bounded = JsonDocument.Parse("[\"1\",null]");
        _ = VersionRangeDeclaration.Read(bounded.RootElement, new(MaximumNodes: 3, MaximumDepth: 2));
        Expect(() => { _ = VersionRangeDeclaration.Read(bounded.RootElement, new(MaximumNodes: 2)); return null; }, "Version.RangeDeclarationLimitExceeded");
        using var memberCase = JsonDocument.Parse("{\"ANY\":\"1\"}");
        if (VersionRanges.Evaluate("1", VersionRangeDeclaration.Read(memberCase.RootElement)) is not null
            || VersionRanges.Evaluate("1", VersionRangeDeclaration.Read(memberCase.RootElement, memberComparer: StringComparer.OrdinalIgnoreCase)) != true)
            throw new InvalidOperationException("JSON and literal-table declaration comparers were conflated.");
    }
    private static void Expect(Func<bool?> action, string id)
    {
        try { action(); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return; }
        throw new InvalidOperationException("Expected range error " + id);
    }
}
