using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

internal static class RootManifestContracts
{
    public static void Verify()
    {
        var repeated = RootManifestParser.Parse("{schemaVersion:1,id:'m',version:[12,1.234567891234567],provides:['a','b','c']}", null, "fabric");
        Check(repeated.Mods[0].Version == "12 1.23456789123457" && repeated.Mods[0].Provides.All(alias => alias.Version == repeated.Mods[0].Version), "Repeated typed version projection");
        var missingId = RootManifestParser.Parse("{schemaVersion:1,provides:[false]}", null, "fabric");
        var missingVersion = RootManifestParser.Parse("{schemaVersion:1,provides:['a']}", null, "fabric");
        Check(missingId.Diagnostics[0].Message.Contains("'id'", StringComparison.Ordinal)
            && missingVersion.Diagnostics[0].Message.Contains("'version'", StringComparison.Ordinal), "Version projection preserves actual access stage");
        const string fabric = "{\"schemaVersion\":1,\"id\":\"root\",\"version\":\"1\",\"depends\":{\"Keys\":\"*\",\"other\":\"1.x\"},\"provides\":[12,\"12\",\"Keys\",true,[1],[1],{}]}";
        var result = RootManifestParser.Parse(fabric, null, "FABRIC");
        var mod = result.Mods.Single();
        if (result.Status != RootManifestStatus.Complete || !result.CanTraverseNested || mod.Side != ""
            || mod.Requirements.Length != 2 || mod.Requirements[0].Target != "Keys" || mod.Requirements[1].Target != "other"
            || mod.Provides.Length != 7 || mod.Provides[0].Key.Kind != ManifestAliasKeyKind.Int64
            || mod.Provides[1].Key.Kind != ManifestAliasKeyKind.String || mod.Provides[4].Key.Equals(mod.Provides[5].Key))
            throw new InvalidOperationException("Root declaration ownership, typed aliases or E32 enumeration changed.");
        var dates = RootManifestParser.Parse("{schemaVersion:1,id:'2000-01-01T00:00:00Z',version:1,depends:{date:'2000-01-01T00:00:00Z',number:NaN,text:'NaN'}}", null, "fabric");
        if (dates.Mods[0].Id != "01/01/2000 00:00:00" || VersionRanges.Evaluate("NaN", dates.Mods[0].Requirements[0].Range) is not null
            || VersionRanges.Evaluate("NaN", dates.Mods[0].Requirements[1].Range) is not null
            || VersionRanges.Evaluate("NaN", dates.Mods[0].Requirements[2].Range) != true)
            throw new InvalidOperationException("Manifest token types were converted into false range matches.");
        const string quilt = "{quilt_loader:{id:'q',version:'1',depends:[{id:'d',optional:[false,true],any:['a'],all:['b'],unless:'c'}],jars:['nested.jar']}}";
        var partial = RootManifestParser.Parse("invalid", quilt, "quilt");
        if (partial.Mods.Length != 1 || partial.Mods[0].Requirements[0].Kind != "optional"
            || partial.Mods[0].Requirements[0].Any.Length != 1 || partial.Mods[0].Requirements[0].All.Length != 1
            || partial.Mods[0].Requirements[0].Unless?.Target != "c"
            || partial.Status != RootManifestStatus.Incomplete || partial.CanTraverseNested || partial.NestedPaths.Single() != "nested.jar")
            throw new InvalidOperationException("Quilt precedence, recursive fields or retained late-failure prefix changed.");
        var bounded = RootManifestParser.Parse(fabric, null, "fabric", new(MaximumAliases: 1));
        if (bounded.Mods.Length != 0 || bounded.Diagnostics.Single().Code != "DeclarationLimit")
            throw new InvalidOperationException("Declaration quota returned a usable partial root.");
        var nestedEquality = RootManifestParser.Parse(null,
            "{schema_version:1,quilt_loader:{id:'q',version:'1',depends:[{id:'x',optional:[[true],false]}]}}", "quilt");
        if (nestedEquality.Mods[0].Requirements[0].Kind != "required")
            throw new InvalidOperationException("Nested arrays acquired scalar Boolean equality.");
        var schemaArray = RootManifestParser.Parse("{schemaVersion:[[1]],id:'m',version:'1'}", null, "fabric");
        if (schemaArray.Diagnostics.Single().Code != "UnknownSchema")
            throw new InvalidOperationException("Schema filtering recursively compared array elements.");
        try { _ = RootManifestParser.Parse(null, null, "fabric", new(MaximumJsonDepth: 65)); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Metadata.InvalidDeclarationPolicy") { return; }
        throw new InvalidOperationException("Unsafe manifest depth override was accepted.");
    }
    private static void Check(bool value, string context)
    { if (!value) throw new InvalidOperationException(context); }
}
