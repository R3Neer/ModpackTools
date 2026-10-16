using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ContentClassificationContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpt-classify-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "pack with spaces"); Directory.CreateDirectory(Path.Combine(root, ".modpack")); Directory.CreateDirectory(Path.Combine(root, "mods"));
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Fixture\"\n[index]\nfile=\"index.toml\"\n");
            File.WriteAllText(Path.Combine(root, "mods", "fixture.pw.toml"), "name=\"Fixture mod\"\nfilename=\"fixture.jar\"\nside=\"both\"\n[update.modrinth]\nmod-id=\"m1\"\nversion=\"v1\"\n");
            var metadata = Path.Combine(root, ".modpack", "metadata.psd1");
            File.WriteAllText(metadata, "@{Categories=@{quality=@{Name='Quality'}};Mods=@{'modrinth:m1'=@{Name='Override';Side='client';Unknown=@{Empty=@();Rows=@(@('a','b'),@('c'))}}};ResourcePacks=@{}}");
            var project = ProjectReader.Read(root); var config = Path.Combine(directory, "config"); var state = Path.Combine(directory, "state");
            var now = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
            var request = new ContentClassificationRequest(["modrinth:m1", "fixture"], "quality"); var before = Fingerprint();
            var preview = await Execute(request with { DryRun = true });
            Assert(!preview.Applied && preview.Changes.Length == 1 && preview.Changes[0].Path == ".modpack/metadata.psd1" && Fingerprint() == before, "classification preview changes only editorial metadata and protects source");
            var applied = await ContentClassification.ExecuteAsync(project, request, config, state, DateTimeOffset.UtcNow, expected: preview.Expectation);
            var assigned = PsdReader.Parse(File.ReadAllText(metadata));
            var assignedEntry = (PsdTable)((PsdTable)assigned.Entries["Mods"]).Entries["modrinth:m1"];
            Assert(applied.Applied && assignedEntry.Entries["Category"] is PsdString { Value: "quality" }, "batch classification publishes expected changes");
            var cleared = await Execute(request with { Category = "unclassified" });
            Assert(cleared.Applied, "clearing retained assignment applies");
            using var cache = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "last-categories.json")));
            Assert(cache.RootElement.GetProperty("CreatedUtc").GetDateTimeOffset() == now, "cache refresh uses the owned invocation clock");
            var data = PsdReader.Parse(File.ReadAllText(metadata));
            var mods = (PsdTable)data.Entries["Mods"]; var entry = (PsdTable)mods.Entries["modrinth:m1"];
            Assert(!entry.Entries.ContainsKey("Category") && entry.Entries.ContainsKey("Name") && entry.Entries.ContainsKey("Side") && entry.Entries.ContainsKey("Unknown"), "clearing preserves unrelated override fields");
            var unknown = (PsdTable)entry.Entries["Unknown"]; var rows = (PsdArray)unknown.Entries["Rows"];
            Assert(((PsdArray)unknown.Entries["Empty"]).Items.IsEmpty && rows.Items.Length == 2
                && ((PsdArray)rows.Items[0]).Items.SequenceEqual([new PsdString("a"), new PsdString("b")])
                && ((PsdArray)rows.Items[1]).Items.SequenceEqual([new PsdString("c")]), "nested and empty unknown arrays survive classification");
            before = Fingerprint();
            try { _ = await Execute(request with { Category = "missing", Selectors = ["999"] }); throw new Exception("Unknown category accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Metadata.UnknownCategory") { }
            Assert(Fingerprint() == before, "category validation precedes invalid reference without source writes");
            try
            {
                _ = await ContentClassification.ExecuteAsync(project, request, Path.Combine(root, "configuration"), state, DateTimeOffset.UtcNow);
                throw new Exception("Protected-tree cache directory accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.UnsafeConfigurationDirectory") { }
            Assert(Fingerprint() == before && !Directory.Exists(Path.Combine(root, "configuration")), "unsafe configuration rejected before effects");
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
            Task<ModpackTools.Core.Transactions.TransactionResult<string>> Execute(ContentClassificationRequest value)
                => ContentClassification.ExecuteAsync(project, value, config, state, now);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Classification contract: " + message); }
}
