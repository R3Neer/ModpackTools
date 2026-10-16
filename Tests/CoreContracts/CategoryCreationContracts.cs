using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class CategoryCreationContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpt-category-create-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "pack with spaces"); Directory.CreateDirectory(Path.Combine(root, ".modpack"));
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Fixture\"\n[index]\nfile=\"index.toml\"\n");
            var path = Path.Combine(root, ".modpack", "metadata.psd1");
            File.WriteAllText(path, "@{Categories=@{quality=@{Name='Quality'};utility=@(@{Name='Utility';Order=-20})};Mods=@{};ResourcePacks=@{};Unknown=@{Empty=@();Flag=$true}}");
            var project = ProjectReader.Read(root); var config = Path.Combine(directory, "config"); var state = Path.Combine(directory, "state"); var before = Fingerprint();
            try
            {
                _ = await CategoryCreation.ExecuteAsync(project, new("new-category"), config, state, DateTimeOffset.UtcNow,
                    prepareOutput: (_, _) => throw new IOException("fixture output preparation fails"));
                throw new Exception("Output failure was accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.IoFailed" && error.Details == "fixture output preparation fails") { }
            Assert(Fingerprint() == before && !File.Exists(Path.Combine(config, "last-categories.json")), "output preparation failure occurs before source or cache publication");
            var created = await CategoryCreation.ExecuteAsync(project, new("New-Category", " \t"), config, state, DateTimeOffset.UtcNow);
            var row = created.Result.Categories.Single(item => item.Id == "New-Category");
            Assert(created.Applied && row.Name == "NEW-CATEGORY" && row.Order == -10 && created.Result.Project.Root == project.Root, "explicit orders only determine automatic order, uppercase ID and owned result preserved");
            var data = PsdReader.Parse(File.ReadAllText(path));
            Assert(((PsdArray)((PsdTable)data.Entries["Unknown"]).Entries["Empty"]).Items.IsEmpty, "unknown nested empty data preserved");
            before = Fingerprint();
            try { _ = await CategoryCreation.ExecuteAsync(project, new("new-category"), config, state, DateTimeOffset.UtcNow); throw new Exception("Duplicate category accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Metadata.CategoryAlreadyExists") { }
            Assert(Fingerprint() == before, "case-insensitive duplicate protects source");
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Category creation contract: " + message); }
}
