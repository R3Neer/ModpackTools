using ModpackTools.Core.Build;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Source;

internal static class ArtifactFreshnessContracts
{
    public static async Task VerifyAsync(string packwiz)
    {
        if (!File.Exists(packwiz)) throw new ArgumentException("Supply an existing native Packwiz executable.");
        var home = Path.Combine(Path.GetTempPath(), "mpt-freshness-" + Guid.NewGuid().ToString("N")); var root = Path.Combine(home, "pack with spaces");
        Directory.CreateDirectory(Path.Combine(root, ".modpack")); Directory.CreateDirectory(Path.Combine(root, "config"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='freshness-fixture';OutputName='fixture.mrpack'}");
            File.WriteAllText(Path.Combine(root, ".modpack", "metadata.psd1"), "@{Mods=@{}}");
            var manifest = Path.Combine(root, "pack.toml");
            File.WriteAllText(manifest, "name=\"Freshness\"\nversion=\"1\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"1.21\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "index.toml"), "hash-format=\"sha256\"\nfiles=[]\n");
            File.WriteAllText(Path.Combine(root, ".packwizignore"), ".modpack/**\n"); File.WriteAllText(Path.Combine(root, "config", "fixture.txt"), "same\n");
            var project = ProjectReader.Read(root); var config = Path.Combine(home, "configuration"); var state = Path.Combine(home, "state");
            var badTool = Path.Combine(home, "not a native executable.exe"); File.WriteAllText(badTool, "not a PE file");
            var built = await BuildOperation.ExecuteAsync(project, new(), config, state, DateTimeOffset.UtcNow, Tool, Environment.ProcessPath!);
            var current = await Read(false);
            Assert(current.State == "current" && !current.FromCache, "uncached complete artifact verified");
            var cached = await Read(true);
            Assert(cached.State == "current" && cached.FromCache, "strong identity hit needs no Packwiz execution");
            File.WriteAllText(manifest, "description=\"changed description\"\n" + File.ReadAllText(manifest));
            var stale = await Read(false);
            Assert(stale.State == "stale" && !stale.FromCache && stale.Comparison.Changed.Single().Path == "summary", "changed summary cannot be called current");
            var newer = Path.Combine(root, "dist", "new-latest.mrpack"); File.Copy(built.Result.Path, newer); File.SetLastWriteTimeUtc(newer, File.GetLastWriteTimeUtc(built.Result.Path).AddMinutes(1));
            var changedSelection = await Read(true);
            Assert(changedSelection.State == "unknown" && !changedSelection.FromCache, "a newly selected baseline invalidates a strong cache hit");
            ModpackTools.Core.Processes.PackwizObservation Tool() => new(true, packwiz, "fixture", null);
            async Task<ArtifactFreshnessResult> Read(bool failTool)
            {
                var before = SourceFingerprint.All(SourceTree.Read(root));
                var result = await ArtifactFreshness.ReadAsync(project, config, state, failTool ? () => new(true, badTool, "fixture", null) : Tool, Environment.ProcessPath!);
                Assert(SourceFingerprint.All(SourceTree.Read(root)) == before, "freshness leaves every original byte unchanged"); return result;
            }
        }
        finally { Directory.Delete(home, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Artifact freshness contract: " + message); }
}
