using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;

internal static class ResourceTargetContracts
{
    public static void Verify(RawInventorySnapshot snapshot)
    {
        var metadata = snapshot.Metadata with { ResourcePacks = PsdReader.Parse("@{extra=@{Name=$null};''=@{Name='Empty key'}}") };
        var inactive = new InventoryItem("inactive", ContentKind.ResourcePack, "Inactive", null, "unknown", ContentSource.Local, null, null, null);
        var one = snapshot with { Metadata = metadata, ResourceView = new([new("vanilla", "Vanilla", null, ActiveResourceSource.Builtin, 1)], [inactive]) };
        var selected = ResourcePackSelector.Resolve(one, ["VANILLA", "file/", "Empty key", "extra", "Vanilla"]);
        if (selected.Length != 5 || selected[0] != new ResourcePackTarget("vanilla", "vanilla", "Vanilla", null, true) ||
            selected[1] != new ResourcePackTarget("file/", "inactive", "Inactive", null, false) ||
            selected[2] != new ResourcePackTarget("", "", "Empty key", null, false) ||
            selected[3] != new ResourcePackTarget("extra", "extra", "", null, false) || !ReferenceEquals(selected[0], selected[4]))
            throw new InvalidOperationException("Resource candidate fields, empty values, local acceptance or repeated outputs changed.");
        Expect(one with { ResourceView = new([one.ResourceView.Active[0], one.ResourceView.Active[0]], []) }, ["vanilla"], "ResourcePack.AmbiguousSelector");
        Expect(one with { ResourceView = new([], []) }, ["extra"], "ResourcePack.InvalidSelectorMetadata");
        Expect(one with { Metadata = metadata with { ResourcePacks = PsdReader.Parse("@{}") }, ResourceView = new([], []) }, ["extra"], "ResourcePack.NotFound");
        var physical = inactive with { Filename = "é.zip" };
        var skipped = one with { Metadata = metadata with { ResourcePacks = PsdReader.Parse("@{'file/é.zip'=$false}") }, ResourceView = new([], [physical]) };
        if (ResourcePackSelector.Resolve(skipped, ["é.zip"])[0].DefaultId != "file/é.zip")
            throw new InvalidOperationException("Cultural represented-key skip accessed malformed metadata.");
        Expect(skipped, ["e\u0301.zip"], "ResourcePack.NotFound");
        foreach (var entry in new[] { "$null", "$false", "@()", "@(@{Name=@()})", "@(@{},@{})", "@(@{Name='A'},@{})" })
            Expect(one with { Metadata = metadata with { ResourcePacks = PsdReader.Parse("@{extra=" + entry + "}") } }, ["vanilla"], "ResourcePack.InvalidSelectorMetadata");
        foreach (var source in new[] { "@{z=@{Name='Grouped'};a=@{Name='Grouped'}}", "@{a=@{Name='Grouped'};z=@{Name='Grouped'}}" })
        {
            try { ResourcePackSelector.Resolve(one with { Metadata = metadata with { ResourcePacks = PsdReader.Parse(source) } }, ["Grouped"]); }
            catch (DomainException exception) when (exception.Id == "ModpackTools.ResourcePack.AmbiguousSelector" && exception.Details == "Matches: 'Grouped' [a], 'Grouped' [z]") { continue; }
            throw new InvalidOperationException("Editorial resource ambiguity order changed with input insertion.");
        }
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            foreach (var source in new[] { "@{'I­'=@{Name='Grouped'};i=@{Name='Grouped'}}", "@{i=@{Name='Grouped'};'I­'=@{Name='Grouped'}}" })
            {
                var imported = metadata with { ResourcePacks = PsdReader.Parse(source) };
                var representative = ResourcePackSelector.Resolve(one with { Metadata = imported }, ["Grouped"]);
                if (representative.Length != 1 || representative[0].DefaultId != "I\u00ad")
                    throw new InvalidOperationException("Editorial invariant equivalence lost its explicit ordinal representative.");
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static void Expect(RawInventorySnapshot snapshot, ImmutableArray<string> selectors, string id)
    {
        try { ResourcePackSelector.Resolve(snapshot, selectors); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return; }
        throw new InvalidOperationException("Expected resource target error " + id);
    }
}
