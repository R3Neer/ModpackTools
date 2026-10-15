using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ContentPinningContracts
{
    public static async Task VerifyAsync(string packwiz)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpt-pin-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "pack with spaces"); Directory.CreateDirectory(Path.Combine(root, ".modpack"));
            Directory.CreateDirectory(Path.Combine(root, "resourcepacks", "custom"));
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Fixture\"\npack-format=\"packwiz:1.1.0\"\n[index]\nfile=\"custom.index.toml\"\nhash-format=\"sha256\"\nhash=\"stale\"\n[versions]\nminecraft=\"1.21.1\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "custom.index.toml"), "hash-format=\"sha256\"\nfiles=[]\n");
            var path = Path.Combine(root, "resourcepacks", "custom", "resource.pw.toml");
            const string declaration = "name=\"Resource\"\nfilename=\"resource.zip\"\nside=\"client\"\nunknown=\"preserve $&\"\n[download]\nurl=\"https://example.invalid/resource.zip\"\nhash-format=\"sha1\"\nhash=\"0000000000000000000000000000000000000000\"\n[update.modrinth]\nmod-id=\"r1\"\nversion=\"v1\"\n";
            File.WriteAllText(path, declaration);
            var project = ProjectReader.Read(root); var config = Path.Combine(directory, "config"); var state = Path.Combine(directory, "state");
            var request = new ContentPinningRequest(["modrinth:r1", "resource", "Resource"], true);
            var before = Fingerprint();
            var preview = await Execute(request with { DryRun = true });
            Assert(!preview.Applied && preview.Result.SequenceEqual(["modrinth:r1"]) && Fingerprint() == before, "deduplicated dry-run preserves protected root");
            var applied = await ContentPinning.ExecuteAsync(project, request, config, state, DateTimeOffset.UtcNow, Tool, Environment.ProcessPath!, expected: preview.Expectation);
            Assert(applied.Applied && InventoryReader.Read(project).Resources.Single().Pinned == true && File.ReadAllText(path).Contains("preserve $&", StringComparison.Ordinal), "native pin preserves unknown values and public state");
            var unpin = await Execute(request with { Pinned = false });
            Assert(unpin.Applied && InventoryReader.Read(project).Resources.Single().Pinned == false, "native unpin is reflected in inventory");
            File.WriteAllText(path, "\"pin\"=false\n" + declaration); before = Fingerprint();
            try { _ = await Execute(request); throw new Exception("Quoted pin was accepted with a contradictory lexical state."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Content.UnsupportedPinEdit") { }
            Assert(Fingerprint() == before, "unrepresentable public pin state leaves source unchanged");
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
            PackwizObservation Tool() => new(true, packwiz, "fixture", null);
            Task<ModpackTools.Core.Transactions.TransactionResult<System.Collections.Immutable.ImmutableArray<string>>> Execute(ContentPinningRequest value)
                => ContentPinning.ExecuteAsync(project, value, config, state, DateTimeOffset.UtcNow, Tool, Environment.ProcessPath!);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Content pinning contract: " + message); }
}
