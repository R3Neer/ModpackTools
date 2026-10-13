using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class ClassificationTargetContracts
{
    public static void Verify(RawInventorySnapshot snapshot)
    {
        var row = new InventoryItem("same", ContentKind.Mod, "same", "same", "unknown", ContentSource.Local, null, null, null);
        var one = snapshot with { Mods = [row], Resources = [row with { Kind = ContentKind.ResourcePack }], Shaders = [] };
        var result = ModClassificationSelector.Resolve(one, ["SAME", "same", "same"]);
        if (result.Length != 3 || result.Any(item => !ReferenceEquals(item, row)))
            throw new InvalidOperationException("Classification discarded local content, repeated requests or original row identity.");
        foreach (var rows in new ImmutableArray<InventoryItem>[] { [row, row], [row, row with { }] })
            Expect(one with { Mods = rows }, ["same"], "Metadata.AmbiguousMod");
        Expect(one with { Mods = [] }, ["same"], "Metadata.ModNotFound");
        Expect(one, ["same", "absent", "same"], "Metadata.ModNotFound");
        var unicode = row with { Id = "é", Name = "A", Filename = null };
        Expect(one with { Mods = [unicode] }, ["e\u0301"], "Metadata.ModNotFound");
        var newline = row with { Name = "A", MetadataPath = "C:\\fixture\\mods\\name.pw.toml\n" };
        if (!ReferenceEquals(ModClassificationSelector.Resolve(one with { Mods = [newline] }, ["name\n"])[0], newline))
            throw new InvalidOperationException("Classification changed the one-final-LF stem rule.");
        foreach (var ending in new[] { "\r\n", "\n\n" })
        {
            var unchanged = newline with { MetadataPath = "C:\\fixture\\mods\\name.pw.toml" + ending };
            if (!ReferenceEquals(ModClassificationSelector.Resolve(one with { Mods = [unchanged] }, ["name.pw.toml" + ending])[0], unchanged))
                throw new InvalidOperationException("Classification removed a stem suffix before CRLF or multiple LFs.");
        }
        var tied = unicode with { Id = "e\u0301" };
        foreach (var rows in new ImmutableArray<InventoryItem>[] { [unicode, tied], [tied, unicode] })
        {
            try { ModClassificationSelector.Resolve(one with { Mods = rows }, ["A"]); }
            catch (DomainException exception) when (exception.Id == "ModpackTools.Metadata.AmbiguousMod" && exception.Details == "Matching IDs: e\u0301") { continue; }
            throw new InvalidOperationException("Classification diagnostic ties changed with row order.");
        }
        foreach (var invalid in new ImmutableArray<string>[] { default, [], [""], [null!], ImmutableArray.CreateRange(Enumerable.Repeat("same", 1025)) })
        {
            try { ModClassificationSelector.Resolve(one, invalid); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Classification input grammar failed.");
        }
    }

    private static void Expect(RawInventorySnapshot snapshot, ImmutableArray<string> selectors, string id)
    {
        try { ModClassificationSelector.Resolve(snapshot, selectors); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return; }
        throw new InvalidOperationException("Expected classification target error " + id);
    }
}
