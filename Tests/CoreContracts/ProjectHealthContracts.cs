using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Source;

internal static class ProjectHealthContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools health " + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "pack"); var config = Path.Combine(directory, "config");
        Directory.CreateDirectory(Path.Combine(root, ".modpack")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        Directory.CreateDirectory(Path.Combine(config, "metadata"));
        var now = DateTimeOffset.UtcNow;
        try
        {
            Write(Path.Combine(root, ".modpack", "project.psd1"), "@{ SchemaVersion=1; Id='health-test' }");
            Write(Path.Combine(root, "pack.toml"), "name=\"Health\"\nversion=\"1\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"1.21\"\nfabric=\"0.16.0\"\n");
            Write(Path.Combine(root, "index.toml"), "hash-format=\"sha256\"\n");
            var jar = Path.Combine(root, "mods", "managed.jar");
            using (var file = new FileStream(jar, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open(), new UTF8Encoding(false)))
                writer.Write("{\"schemaVersion\":1,\"id\":\"native_health\",\"version\":\"1.0.0\",\"depends\":{\"java\":\">=21\"}}");
            var missingJava = await Observe();
            Assert(missingJava.Report.Unknown.Count(issue => issue.Code == "environment.java-undeclared") == 2, "Java presence retained");
            Write(Path.Combine(root, ".modpack", "project.psd1"), "@{ SchemaVersion=1; Id='health-test'; JavaVersion='21' }");
            Assert((await Observe()).Report.Issues.IsEmpty, "complete local health");
            Directory.CreateDirectory(Path.Combine(root, "config", "fabric_loader_dependencies.json"));
            Assert((await Observe()).Report.Unknown.Any(issue => issue.Code == "verification.runtime-override"), "override directory presence");
            Directory.Delete(Path.Combine(root, "config", "fabric_loader_dependencies.json"));
            var payload = File.ReadAllBytes(jar); var hash = Convert.ToHexString(SHA512.HashData(payload));
            Directory.CreateDirectory(Path.Combine(config, "artifacts"));
            File.WriteAllBytes(Path.Combine(config, "artifacts", TextHash.Compute("sha512:" + hash)), payload);
            var metadata = Path.Combine(root, "mods", "managed.pw.toml");
            Write(metadata, "name=\"Managed\"\nfilename=\"managed.jar\"\nside=\"both\"\n  pin = true\n[update.modrinth]\nmod-id=\"p1\"\nversion=\"v1\"\n");
            Write(Path.Combine(root, ".modpack", "metadata.psd1"), "@{ContentSchemaVersion=1;Content=@{'modrinth:p1'=@{Intent='transitive'}}}");
            Cache("version/v1", Version("v1", "p1"));
            var unknown = await Observe();
            Assert(unknown.Report.Errors.Length == 2 && unknown.Report.Complete, "unknown provider availability is not no candidates");
            Assert(unknown.Nodes[0].Pinned && unknown.Nodes[0].Intent == "transitive" && unknown.Inventory.Mods[0].Pinned == false, "semantic facts versus lexical public pin");
            Cache("project/p3", "{\"id\":\"p3\",\"project_type\":\"mod\"}");
            Cache(new ProviderProject("p3", "", "mod", [], "", "").CandidateEndpoint("1.21", "fabric"), "[]");
            var empty = await Observe();
            Assert(empty.Report.Errors.IsEmpty && empty.Report.Unknown.Length == 2 && empty.Report.Unknown.All(issue => issue.Code == "provider.no-compatible-candidate"), "known-empty reconciliation");
            Assert(empty.Report.Issues.Select(issue => issue.Key).SequenceEqual(unknown.Report.Issues.Select(issue => issue.Key)), "reclassification preserves key2");
            Write(metadata, File.ReadAllText(metadata).Replace("[update.modrinth]", "[option]\noptional=true\ndefault=false\n[update.modrinth]", StringComparison.Ordinal));
            Assert(!(await Observe()).Nodes[0].Graph.Enabled, "default optional selection");
            Write(Path.Combine(root, "mods", "second.pw.toml"), "name=\"Second\"\nfilename=\"second.jar\"\n[update.modrinth]\nmod-id=\"p2\"\nversion=\"v2\"\n");
            Cache("version/v2", Version("v2", "p2"));
            try { await Observe(new(Models: new(MaximumVersions: 1))); throw new InvalidOperationException("Expected aggregate provider version limit."); }
            catch (DomainException error) { Assert(error.Id == "ModpackTools.Metadata.ProviderModelLimit", "aggregate model limit before second retained version"); }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await ProjectHealth.ReadAsync(ProjectReader.Read(root), config, now, cancellation: cancellation.Token); throw new InvalidOperationException("Expected cancellation."); }
            catch (DomainException error) { Assert(error.Id == "ModpackTools.Core.OperationCancelled", "cancellation stays domain failure"); }

            string Version(string id, string project) => "{\"id\":\"" + id + "\",\"project_id\":\"" + project + "\",\"date_published\":\"2026-10-12T10:00:00Z\",\"files\":[{\"filename\":\"managed.jar\",\"hashes\":{\"sha512\":\"" + hash + "\"}}],\"dependencies\":[{\"project_id\":\"p3\",\"dependency_type\":\"required\"}]}";
        }
        finally { Directory.Delete(directory, recursive: true); }
        Task<ProjectHealthObservation> Observe(ProjectHealthLimits? limits = null) => ProjectHealth.ReadAsync(ProjectReader.Read(root), config, now, limits: limits);
        void Cache(string endpoint, string text)
        { var path = Path.Combine(config, "metadata", TextHash.Compute(endpoint) + ".json"); Write(path, text); File.SetLastWriteTimeUtc(path, now.UtcDateTime); }
        static void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Project health: " + message); }
    }
}
