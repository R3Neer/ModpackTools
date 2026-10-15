using System.Security.Cryptography;
using ModpackTools.Core;
using ModpackTools.Core.Build;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Source;

internal static class ProjectIndexContracts
{
    public static void Verify()
    {
        var root = Path.Combine(Path.GetTempPath(), "mpt-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".modpack")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='index-fixture'}");
            File.WriteAllText(Path.Combine(root, "README.md"), "owned fixture\n");
            var metadata = Path.Combine(root, "mods", "fixture.pw.toml"); File.WriteAllText(metadata, "name=\"Fixture\"\nfilename=\"fixture.jar\"\n");
            var row = Row("mods/fixture.pw.toml", true); var readme = Row("README.md", false);
            Write(row + readme); var project = ProjectReader.Read(root); var before = Fingerprint();
            Assert(ProjectIndex.Read(project) is { Valid: true, Rows: 2 } && Fingerprint() == before, "complete same-byte index and source integrity");
            File.AppendAllText(metadata, "# changed\n");
            Assert(ProjectIndex.Read(project).Issues.SequenceEqual(["Index hash is stale for mods/fixture.pw.toml"]), "actual row digest checked");
            row = Row("mods/fixture.pw.toml", true); Write(row + row + readme);
            Assert(ProjectIndex.Read(project).Issues.Contains("Index repeats file mods/fixture.pw.toml"), "duplicate path rejected");
            Write(Row("mods/fixture.pw.toml", false) + readme);
            Assert(ProjectIndex.Read(project).Issues.Contains("Index metadata flag is missing for mods/fixture.pw.toml"), "metadata flag required");
            Write(readme);
            Assert(ProjectIndex.Read(project).Issues.SequenceEqual(["Index omits managed metadata mods/fixture.pw.toml"]), "physical metadata coverage");
            Write("");
            Assert(ProjectIndex.Read(project).Issues.SequenceEqual(["Index contains no files", "Index omits managed metadata mods/fixture.pw.toml"]), "empty index never proves freshness");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { _ = ProjectIndex.Read(project, cancellation: cancellation.Token); throw new Exception("Cancelled verification accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Core.OperationCancelled") { }
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
            string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            string Row(string path, bool metadata) => "[[files]]\nfile=\"" + path + "\"\nhash=\"" + Hash(Path.Combine(root, path)) + "\"\nmetafile=" + (metadata ? "true" : "false") + "\n";
            void Write(string rows)
            {
                var index = Path.Combine(root, "index.toml"); File.WriteAllText(index, "hash-format=\"sha256\"\n" + (rows.Length == 0 ? "files=[]\n" : rows));
                File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Index\"\nversion=\"1\"\n[index]\nfile=\"index.toml\"\nhash-format=\"sha256\"\nhash=\"" + Hash(index) + "\"\n[versions]\nminecraft=\"1.21\"\nfabric=\"0.16\"\n");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Project index contract: " + message); }
}
