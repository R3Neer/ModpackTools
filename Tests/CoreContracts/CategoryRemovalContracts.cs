using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class CategoryRemovalContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpt-category-remove-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "pack with spaces"); Directory.CreateDirectory(Path.Combine(root, ".modpack"));
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Fixture\"\n[index]\nfile=\"index.toml\"\n");
            var path = Path.Combine(root, ".modpack", "metadata.psd1");
            File.WriteAllText(path, "@{Categories=@{quality=@{Name='Quality'};utility=@{Name='Utility'}};Mods=@{z=@{Category='quality'};a=@{Category='QUALITY';Name='Keep';Side='client';Unknown=@{Empty=@();Rows=@('one','two')}}};ResourcePacks=@{};Unknown='root'}");
            var project = ProjectReader.Read(root); var config = Path.Combine(directory, "config"); var state = Path.Combine(directory, "state");
            var request = new CategoryRemovalRequest(["quality", "QUALITY"]); var before = Fingerprint();
            try { _ = await Execute(request); throw new Exception("In-use category accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Metadata.CategoryInUse") { Assert(error.Details == "a, z", "stable sorted assignment diagnostic"); }
            Assert(Fingerprint() == before, "in-use error protects complete root");
            request = request with { ClearAssignments = true };
            var preview = await Execute(request with { DryRun = true });
            Assert(!preview.Applied && preview.Result.SequenceEqual(["quality"]) && preview.Changes.Length == 1 && Fingerprint() == before && !File.Exists(Path.Combine(config, "last-categories.json")), "deduplicated preview preserves root and cache");
            var applied = await CategoryRemoval.ExecuteAsync(project, request, config, state, DateTimeOffset.UtcNow, expected: preview.Expectation);
            var data = PsdReader.Parse(File.ReadAllText(path)); var categories = (PsdTable)data.Entries["Categories"]; var mods = (PsdTable)data.Entries["Mods"];
            var kept = (PsdTable)mods.Entries["a"]; var unknown = (PsdTable)kept.Entries["Unknown"];
            Assert(applied.Applied && !categories.Entries.ContainsKey("quality") && categories.Entries.ContainsKey("utility") && !mods.Entries.ContainsKey("z") && !kept.Entries.ContainsKey("Category"), "remove definition, assignment and only empty rows");
            Assert(kept.Entries["Name"] is PsdString { Value: "Keep" } && kept.Entries.ContainsKey("Side") && ((PsdArray)unknown.Entries["Empty"]).Items.IsEmpty && data.Entries["Unknown"] is PsdString { Value: "root" }, "unknown nested and empty fields survive");
            using var cache = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "last-categories.json")));
            Assert(cache.RootElement.GetProperty("Categories").EnumerateArray().All(row => row.GetProperty("Id").GetString() != "quality"), "cache excludes removed category");
            File.WriteAllText(path, "not a PSD file"); before = Fingerprint();
            try { _ = await Execute(new(["unclassified"])); throw new Exception("Reserved category accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Metadata.ReservedClassification") { }
            Assert(Fingerprint() == before, "reserved selector wins before corrupt metadata IO");
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
            Task<ModpackTools.Core.Transactions.TransactionResult<ImmutableArray<string>>> Execute(CategoryRemovalRequest value)
                => CategoryRemoval.ExecuteAsync(project, value, config, state, DateTimeOffset.UtcNow);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Category removal contract: " + message); }
}
