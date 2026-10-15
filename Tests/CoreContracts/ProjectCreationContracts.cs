using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Source;

internal static class ProjectCreationContracts
{
    public static async Task VerifyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "mpt-create-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "sentinel.txt"), "existing catalog bytes");
            var before = SourceFingerprint.All(SourceTree.Read(root)); var host = Environment.ProcessPath!;
            try
            {
                _ = await ProjectCreation.ExecuteAsync(root, new("fixture", "Fixture Pack", "1.21.1", "fabric", "0.16.10"), new(true, host, "fixture", null), host,
                    prepareOutput: (_, _) => throw new IOException("fixture output preparation fails"));
                throw new Exception("Creation accepted output failure.");
            }
            catch (IOException error) when (error.Message == "fixture output preparation fails") { }
            Assert(SourceFingerprint.All(SourceTree.Read(root)) == before && Directory.GetDirectories(root).Length == 0, "failed output leaves no project or workspace");
            var created = await ProjectCreation.ExecuteAsync(root, new("fixture", "Fixture Pack", "1.21.1", "fabric", "0.16.10"), new(true, host, "fixture", null), host);
            Assert(created.Project.Root == Path.Combine(root, "Fixture Pack-1.21.1") && created.Diagnostics.IsEmpty, "native creation final project and cleanup");
            Assert(ProjectReader.Read(created.Project.Root).Name == "Fixture Pack" && Directory.Exists(Path.Combine(created.Project.Root, "dist")), "created identity and standard empty directories");
            before = SourceFingerprint.All(SourceTree.Read(root));
            try { _ = await ProjectCreation.ExecuteAsync(root, new("fail", "Fail Pack", "1.21.1", "fabric"), new(true, host, "fixture", null), host); throw new Exception("Tool failure accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Build.ProcessFailed") { }
            Assert(SourceFingerprint.All(SourceTree.Read(root)) == before && !Directory.Exists(Path.Combine(root, "Fail Pack-1.21.1")), "partial tool writes never publish");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Creation contract: " + message); }
}
