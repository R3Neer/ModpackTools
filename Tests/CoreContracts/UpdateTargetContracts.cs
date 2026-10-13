using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class UpdateTargetContracts
{
    public static void Verify(RawInventorySnapshot snapshot)
    {
        var row = new InventoryItem("same", ContentKind.Mod, "same", "same", "unknown",
            ContentSource.Packwiz, null, @"C:\fixture\mods\same.PW.TOML", null);
        var one = snapshot with { Mods = [row], Resources = [], Shaders = [] };
        var selected = UpdateTargetSelector.Resolve(one, ["SAME"], "mods");
        if (selected.Length != 1 || !ReferenceEquals(selected[0], row))
            throw new InvalidOperationException("Aliases on one occurrence changed identity or became ambiguous.");
        foreach (ImmutableArray<InventoryItem> rows in new ImmutableArray<InventoryItem>[] { [row, row], [row, row with { }] })
            Expect(one with { Mods = rows }, ["same"], "Content.AmbiguousSelector");
        Expect(one, ["Display"], "Content.NotFound");
        Expect(one, ["vanilla"], "Content.NotFound");
        var newline = row with { MetadataPath = "C:\\fixture\\mods\\weird.pw.toml\n" };
        if (!ReferenceEquals(UpdateTargetSelector.Resolve(one with { Mods = [newline] }, ["weird\n"])[0], newline))
            throw new InvalidOperationException("Legacy stem suffix lost its final LF anchor behavior.");
        foreach (var ending in new[] { "\r\n", "\n\n" })
        {
            var unchanged = newline with { MetadataPath = "C:\\fixture\\mods\\weird.pw.toml" + ending };
            if (!ReferenceEquals(UpdateTargetSelector.Resolve(one with { Mods = [unchanged] }, ["weird.pw.toml" + ending])[0], unchanged))
                throw new InvalidOperationException("Stem suffix wrongly consumed CRLF or multiple LFs.");
        }

        var first = row with { Id = "id:first", Name = "é", Filename = "first.jar", MetadataPath = @"C:\fixture\mods\é.pw.toml" };
        var second = row with { Id = "id:second", Name = "Second", Filename = "second.jar", MetadataPath = "C:\\fixture\\mods\\e\u0301.pw.toml" };
        var pair = one with { Mods = [first, second] };
        selected = UpdateTargetSelector.Resolve(pair, [second.Id, first.Id, second.Id]);
        if (selected.Length != 1 || !ReferenceEquals(selected[0], second))
            throw new InvalidOperationException("Metadata path dedup changed culture or first selector ownership.");
        Expect(one with { Mods = [first] }, ["e\u0301"], "Content.NotFound");
        Expect(pair with { Mods = [first, second with { Source = ContentSource.Local }] }, [first.Id, second.Id], "Content.LocalNotUpdatable");
        var whitespace = row with { Name = " ", MetadataPath = " ", Id = "space", Filename = null };
        if (!ReferenceEquals(UpdateTargetSelector.Resolve(one with { Mods = [whitespace] }, [" "])[0], whitespace))
            throw new InvalidOperationException("Whitespace aliases or paths were silently discarded.");

        var upper = row with { Id = "MODRINTH:a", Name = "collision", MetadataPath = "upper" };
        var lower = upper with { Id = "modrinth:A", MetadataPath = "lower" };
        var error = Expect(one with { Mods = [lower, upper] }, ["collision"], "Content.AmbiguousSelector");
        var reversed = Expect(one with { Mods = [upper, lower] }, ["collision"], "Content.AmbiguousSelector");
        if (error.Details != "Matching entries: mod:MODRINTH:a" || error.Details != reversed.Details)
            throw new InvalidOperationException("E16 ambiguity representative is not deterministic.");
        try { UpdateTargetSelector.Resolve(one, ["same"], null); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Inventory.InvalidType" && exception.Target is TextTarget { Value: "" }) { return; }
        throw new InvalidOperationException("Explicit null update type was silently defaulted.");
    }

    private static DomainException Expect(RawInventorySnapshot snapshot, ImmutableArray<string> selectors, string id)
    {
        try { UpdateTargetSelector.Resolve(snapshot, selectors); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return exception; }
        throw new InvalidOperationException("Expected pure selector failure: " + id);
    }
}
