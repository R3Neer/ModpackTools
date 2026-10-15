using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ResourceOperationContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpt-resources-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "pack with spaces"); Directory.CreateDirectory(Path.Combine(root, ".modpack"));
            Directory.CreateDirectory(Path.Combine(root, "mods")); Directory.CreateDirectory(Path.Combine(root, "resourcepacks")); Directory.CreateDirectory(Path.Combine(root, "config"));
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Fixture\"\n[index]\nfile=\"index.toml\"\n");
            File.WriteAllText(Path.Combine(root, "mods", "defaultoptions.pw.toml"), "name=\"Default Options\"\nfilename=\"defaultoptions.jar\"\n[update.modrinth]\nmod-id=\"WEg59z5b\"\nversion=\"v1\"\n");
            foreach (var id in new[] { "a", "b", "c" }) File.WriteAllText(Path.Combine(root, "resourcepacks", id + ".pw.toml"), $"name=\"Pack {id}\"\nfilename=\"{id}.zip\"\n[update.modrinth]\nmod-id=\"{id}\"\nversion=\"v1\"\n");
            var path = Path.Combine(root, "config", "defaultoptions-common.toml");
            File.WriteAllText(path, "# keep me\ndefaultResourcePacks=[\"file/b.zip\",\"vanilla\",\"file/a.zip\"] # keep tail\nunknown=\"$&\"\n");
            var project = ProjectReader.Read(root); var config = Path.Combine(directory, "config"); var state = Path.Combine(directory, "state");
            var request = new ResourcePackOperationRequest(ResourcePackOperation.Enable, ["b.zip", "Pack b", "c.zip"], 2); var before = Fingerprint();
            var preview = await Execute(request with { DryRun = true });
            Assert(!preview.Applied && preview.Result.Order.SequenceEqual(["file/a.zip", "file/b.zip", "file/c.zip", "vanilla"]) && Fingerprint() == before, "dry block removes selected before positioning and deduplicates aliases");
            var applied = await ResourcePackOperationRunner.ExecuteAsync(project, request, config, state, DateTimeOffset.UtcNow, expected: preview.Expectation);
            Assert(applied.Applied && applied.Changes.Length == 1 && applied.Changes[0].Path == "config/defaultoptions-common.toml", "only order configuration changes");
            Assert(ResourcePackOrderReader.Read(project).SequenceEqual(preview.Result.Order) && File.ReadAllText(path).Contains("# keep tail\nunknown=\"$&\"", StringComparison.Ordinal), "reverse storage and unknown syntax retained");
            var disabled = await Execute(new(ResourcePackOperation.Disable, ["b.zip", "c.zip"]));
            Assert(disabled.Applied && disabled.Result.Order.SequenceEqual(["file/a.zip", "vanilla"]), "disable removes whole block preserving remaining order");
            Assert(disabled.Result.Resources.Inactive.All(item => item.MetadataPath is null || item.MetadataPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), "owned result metadata paths refer to protected source, never temporary stage");
            before = Fingerprint();
            try { _ = await Execute(new(ResourcePackOperation.Move, ["b.zip"], 999)); throw new Exception("Disabled move accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.ResourcePack.NotEnabled") { }
            Assert(Fingerprint() == before, "not enabled wins over invalid position and protects root");
            File.Delete(path); before = Fingerprint();
            var noOp = await Execute(new(ResourcePackOperation.Disable, ["b.zip"]));
            Assert(!noOp.Applied && noOp.Changes.IsEmpty && Fingerprint() == before && !File.Exists(path), "no-op without configuration does not create it");
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
            Task<ModpackTools.Core.Transactions.TransactionResult<ResourcePackOperationResult>> Execute(ResourcePackOperationRequest value)
                => ResourcePackOperationRunner.ExecuteAsync(project, value, config, state, DateTimeOffset.UtcNow);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Resource operation contract: " + message); }
}
