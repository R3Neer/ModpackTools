using System.IO.Compression;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Build;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Source;

internal static class BuildContracts
{
    public static async Task VerifyAsync(string packwiz)
    {
        if (!File.Exists(packwiz)) throw new ArgumentException("Supply an existing native Packwiz executable.");
        var home = Path.Combine(Path.GetTempPath(), "mpt-build-" + Guid.NewGuid().ToString("N")); var root = Path.Combine(home, "pack with spaces");
        Directory.CreateDirectory(Path.Combine(root, ".modpack")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        Directory.CreateDirectory(Path.Combine(root, "resourcepacks")); Directory.CreateDirectory(Path.Combine(root, "config"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='build-fixture';OutputName='fixture.mrpack'}");
            File.WriteAllText(Path.Combine(root, ".modpack", "metadata.psd1"), "@{Mods=@{};Unknown=@{Empty=@()}}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Build\"\nversion=\"1\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"1.21\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "index.toml"), "hash-format=\"sha256\"\nfiles=[]\n");
            File.WriteAllText(Path.Combine(root, ".packwizignore"), ".modpack/**\n");
            File.WriteAllText(Path.Combine(root, "config", "fixture.txt"), "preserve these bytes\n");
            Zip("mods/local.jar", "fabric.mod.json", "{\"schemaVersion\":1,\"id\":\"local_fixture\",\"version\":\"1.0.0\",\"environment\":\"*\"}");
            Zip("resourcepacks/local.zip", "pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Local\"}}");
            var project = ProjectReader.Read(root); var before = Fingerprint();
            var preview = await Execute(true);
            Assert(!preview.Applied && preview.Result.Size > 0 && preview.Result.Snapshot.Any(item => item.Path == "mods/local.jar") && Fingerprint() == before,
                "complete preview validates local content without modifying sources");
            try { _ = await Execute(false, fail: true); throw new Exception("Output failure accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.IoFailed" && error.Details == "fixture output preparation fails") { }
            Assert(Fingerprint() == before, "precommit output failure preserves every source byte");
            var applied = await Execute(false);
            Assert(applied.Applied && File.Exists(applied.Result.Path) && ProjectIndex.Read(project).Valid, "complete build and fresh index are committed together");
            Assert(MrpackSnapshot.Compare(applied.Result.Snapshot, MrpackSnapshot.Read(applied.Result.Path)).Total == 0, "published archive equals validated snapshot");
            var previousHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(applied.Result.Path)));
            var kept = await Execute(false, keep: true);
            Assert(kept.Applied && kept.Result.Path != applied.Result.Path && File.Exists(kept.Result.Path)
                && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(applied.Result.Path))) == previousHash, "keep-old preserves previous archive bytes");
            Task<ModpackTools.Core.Transactions.TransactionResult<BuildResult>> Execute(bool dry, bool fail = false, bool keep = false) =>
                BuildOperation.ExecuteAsync(project, new(KeepOld: keep, DryRun: dry), Path.Combine(home, "configuration"), Path.Combine(home, "state"),
                    new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), () => new(true, packwiz, "fixture", null), Environment.ProcessPath!,
                    prepareOutput: fail ? (_, _) => throw new IOException("fixture output preparation fails") : null);
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
            void Zip(string path, string entry, string content)
            {
                using var file = File.Create(Path.Combine(root, path)); using var archive = new ZipArchive(file, ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry(entry).Open(), new UTF8Encoding(false)); writer.Write(content);
            }
        }
        finally { Directory.Delete(home, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Build operation contract: " + message); }
}
