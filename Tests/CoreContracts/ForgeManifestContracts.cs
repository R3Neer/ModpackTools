using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

internal static class ForgeManifestContracts
{
    public static void Verify()
    {
        const string mod = "[[mods]]\nmodId='m'\nversion='1'\n";
        var basic = ForgeRootManifestParser.Parse(new(mod), "FORGE");
        Check(basic.Status == RootManifestStatus.Complete && basic.Mods is [{ Id: "m", Version: "1" }], "Forge root declaration");
        var selected = ForgeRootManifestParser.Parse(new(mod, "broken="), "neoforge");
        Check(selected.Status == RootManifestStatus.Incomplete && selected.Mods.Length == 0 && !selected.CanTraverseNested, "NeoForge selected failure");
        var features = ForgeRootManifestParser.Parse(new(mod + "features={javaVersion='[17,)'}"), "forge");
        Check(features.Mods.Length == 0 && features.Diagnostics is [{ Code: "ManifestIncomplete" }], "Forge original feature pipeline");
        var supported = ForgeRootManifestParser.Parse(new(mod + "features=[{javaVersion='[17,)'}]"), "forge");
        Check(supported.Mods[0].Requirements is [{ Target: "java", Scheme: VersionScheme.Maven }], "Forge singleton feature table");
        var range = ForgeRootManifestParser.Parse(new(mod + "[[dependencies.m]]\nmodId='d'\nmandatory=[false]\nversionRange=['1','2']\nside='CLIENT'"), "forge");
        Check(range.Mods[0].Requirements is [{ Kind: "required", Side: "client", RangeDisplay: "1 OR 2" }] &&
            VersionRanges.Evaluate("1", range.Mods[0].Requirements[0].Range, VersionScheme.Maven) is null, "TOML collection range and equality");
        var prefix = ForgeRootManifestParser.Parse(new(mod, JarJar: "{jars:[{}]}"), "forge");
        Check(prefix.Mods.Length == 1 && !prefix.CanTraverseNested && prefix.Status == RootManifestStatus.Incomplete, "Forge late root prefix");
        var quota = ForgeRootManifestParser.Parse(new(mod + "[[mods]]\nmodId='n'\nversion='2'"), "forge", new() { MaximumMods = 1 });
        Check(quota.Mods.Length == 1 && quota.Diagnostics is [{ Code: "DeclarationLimit" }], "Forge mod construction quota");
        var ordering = ForgeRootManifestParser.Parse(new(mod + "[[dependencies.m]]\nmodId='d'\nordering=true"), "forge");
        Check(ordering.Diagnostics.Length == 0, "Forge typed ordering equality");
    }
    private static void Check(bool value, string context)
    { if (!value) throw new InvalidOperationException("Native contract failed: " + context); }
}
