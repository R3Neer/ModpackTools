using System.IO.Compression;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;
using ModpackTools.Core.Transactions;

internal static class RemovalContracts
{
    public static async Task VerifyAsync(string packwiz)
    {
        if (!File.Exists(packwiz)) throw new ArgumentException("Supply an existing native Packwiz executable.");
        var home = Path.Combine(Path.GetTempPath(), "mpt-removal-" + Guid.NewGuid().ToString("N")); var root = Path.Combine(home, "pack with spaces");
        Directory.CreateDirectory(Path.Combine(root, ".modpack")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        Directory.CreateDirectory(Path.Combine(root, "resourcepacks")); Directory.CreateDirectory(Path.Combine(root, "config"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='remove-fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Removal\"\nversion=\"1\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"1.21\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "index.toml"), "hash-format=\"sha256\"\nfiles=[]\n");
            Jar("remove"); Jar("keep");
            using (var file = File.Create(Path.Combine(root, "resourcepacks", "remove.zip")))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("pack.mcmeta").Open(), new UTF8Encoding(false)))
                writer.Write("{\"pack\":{\"pack_format\":34,\"description\":\"Remove\"}}");
            File.WriteAllText(Path.Combine(root, "config", "defaultoptions-common.toml"), "# preserve\ndefaultResourcePacks=[\"vanilla\",\"file/remove.zip\"]\n");
            var metadata = Path.Combine(root, ".modpack", "metadata.psd1");
            File.WriteAllText(metadata, "@{Mods=@{'local:mods/remove.jar'=@{Name='Remove display'};'local:mods/keep.jar'=@{Unknown=@{Empty=@()}}};Content=@{'local:mods/remove.jar'=@{Intent='explicit'}};ResourcePacks=@{'file/remove.zip'=@{Name='Remove resource'}};Unknown=@{Empty=@()}}");
            var project = ProjectReader.Read(root); var before = Fingerprint();
            var preview = await Execute(true);
            Assert(!preview.Applied && preview.Changes.Any(change => change.After is null) && Fingerprint() == before, "real Packwiz preview leaves all sources unchanged");
            try { _ = await Execute(false, fail: true); throw new Exception("Output failure accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.IoFailed" && error.Details == "fixture output preparation fails") { }
            Assert(Fingerprint() == before, "failed precommit output preserves all sources");
            var applied = await Execute(false, expected: preview.Expectation);
            Assert(applied.Applied && applied.Result.Changes.Length == 2 && applied.Result.Nodes.Single().Graph.Id == "local:mods/keep.jar", "successful removal retains exact remaining node");
            Assert(!File.Exists(Path.Combine(root, "mods", "remove.jar")) && !File.Exists(Path.Combine(root, "resourcepacks", "remove.zip")), "physical deletion applied");
            Assert(ResourcePackOrderReader.Read(project).SequenceEqual(["vanilla"]), "only removed DefaultOptions entry filtered");
            var editorial = EditorialMetadataReader.Read(project);
            Assert(editorial.Mods.Entries.Count == 1 && editorial.Content!.Entries.IsEmpty && editorial.ResourcePacks.Entries.IsEmpty
                && ((PsdArray)((PsdTable)editorial.Data.Entries["Unknown"]).Entries["Empty"]).Items.IsEmpty, "editorial deletion and unknown empty data preserved");
            Task<TransactionResult<RemovalPlan>> Execute(bool dry, bool fail = false, TransactionExpectation? expected = null) =>
                RemovalOperation.ExecuteAsync(project, new(new(["remove.jar", "remove.zip"]), dry), Path.Combine(home, "configuration"), Path.Combine(home, "state"), DateTimeOffset.UtcNow,
                    () => new(true, packwiz, "fixture", null), Environment.ProcessPath!, expected: expected,
                    prepareOutput: fail ? (_, _) => throw new IOException("fixture output preparation fails") : null);
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
            void Jar(string id)
            {
                using var file = File.Create(Path.Combine(root, "mods", id + ".jar")); using var archive = new ZipArchive(file, ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open(), new UTF8Encoding(false));
                writer.Write("{\"schemaVersion\":1,\"id\":\"" + id + "\",\"version\":\"1.0.0\",\"environment\":\"*\"}");
            }
        }
        finally { Directory.Delete(home, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Removal operation contract: " + message); }
}
