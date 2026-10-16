using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Source;

internal static class ProjectRegistrationContracts
{
    public static async Task VerifyAsync()
    {
        var home = Path.Combine(Path.GetTempPath(), "mpt-register-" + Guid.NewGuid().ToString("N"));
        var catalog = Path.Combine(home, "catalog with spaces"); var root = Path.Combine(catalog, "pack á");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Fixture\"\nversion=\"1.0\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"1.21\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "index.toml"), "hash-format=\"sha256\"\nfiles=[]\n");
            File.WriteAllText(Path.Combine(root, "README.md"), "Own README\r\n");
            var before = Fingerprint(); var state = Path.Combine(home, "state");
            try
            {
                _ = await ProjectRegistration.ExecuteAsync(catalog, new("fixture", root), state,
                    prepareOutput: (_, _) => throw new IOException("fixture output preparation fails"));
                throw new Exception("Registration accepted output failure.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.IoFailed" && error.Details == "fixture output preparation fails") { }
            Assert(Fingerprint() == before && !Directory.Exists(Path.Combine(root, ".modpack")), "output failure must precede publication");
            var result = await ProjectRegistration.ExecuteAsync(catalog, new("Fixture", root, "Display á", "v2", "fixture.mrpack"), state);
            Assert(result.Applied && result.Result.CreatedFiles.Length == 3 && result.Result.Project.Root == root, "registration result and created files");
            Assert(File.ReadAllText(Path.Combine(root, "README.md")) == "Own README\r\n", "existing README bytes");
            Assert(ProjectReader.Read(root).Name == "Display á" && File.ReadAllText(Path.Combine(root, ".gitignore")) == "dist/\n", "descriptor and new gitignore");
            before = Fingerprint();
            try { _ = await ProjectRegistration.ExecuteAsync(catalog, new("fixture", root), state); throw new Exception("Already registered accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Project.AlreadyInitialized") { }
            Assert(Fingerprint() == before, "already registered preserves every file");
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
        }
        finally { Directory.Delete(home, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Registration contract: " + message); }
}
