using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ContentMaterializationContracts
{
    public static void Verify()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools native materialization " + Guid.NewGuid().ToString("N"));
        var original = Path.Combine(directory, "observed"); var root = Path.Combine(directory, "stage");
        Directory.CreateDirectory(Path.Combine(root, "mods", "custom")); Directory.CreateDirectory(Path.Combine(root, ".modpack")); Directory.CreateDirectory(original);
        try
        {
            var observed = ContentMaterializationInspection.Context(original); var stage = ContentMaterializationInspection.Context(root);
            var relative = "mods/custom/retained.pw.toml"; var path = Path.Combine(root, relative);
            var installed = "# retained header\r\nname = \"Old\" # retained field comment\r\nfilename = \"old.jar\"\r\nside = \"both\"\r\npin = true\r\ncustom = '''\nfilename = \"apparent\"\n'''\r\n[option]\r\noptional=true\r\ndefault=false\r\n[download]\r\nurl=\"old\"\r\nhash-format=\"sha1\"\r\nhash=\"old\"\r\n[update.modrinth]\r\nmod-id=\"item\"\r\nversion=\"old\"\r\n";
            File.WriteAllText(path, installed);
            File.WriteAllText(Path.Combine(root, ".modpack", "metadata.psd1"), "@{Unknown=@{Nested=@(@('a','b'),@());Flag=$true;Null=$null;Quote='it''s $literal'};Content=@{'modrinth:item'=@{Intent='transitive';Extra='retained'}}}");
            var before = Node("modrinth:item", "old", Path.Combine(original, relative));
            var after = Node("modrinth:item", "new", null) with { Item = before.Item with { Name = "$& literal\"\nline", VersionId = "new" } };
            var report = GraphReportBuilder.Build(new("1.21", "fabric", "0.16"), []);
            var change = new PlannedContentChange(before.Graph.Id, before, after, "requested");
            var plan = new ContentPlan([after], [change], [after.Graph.Id], report, report, ContentPlanOperation.Update);
            var receipt = ContentMaterialization.Apply(observed, stage, plan);
            Assert(receipt.EditorialWritten && receipt.Files.Single().MetadataPath == relative && receipt.Files.Single().VersionId == "new", "custom path rebased and verified receipt");
            var text = File.ReadAllText(path);
            Assert(text.Contains("# retained field comment", StringComparison.Ordinal) && text.Contains("filename = \"apparent\"", StringComparison.Ordinal), "comments and multiline data preserved");
            var managed = ManagedContentReader.Read(stage).Single();
            Assert(managed.Name == after.Item.Name && managed.VersionId == "new" && managed.Pinned, "literal name, selected version and pin");
            var metadata = EditorialMetadataReader.Read(stage);
            var unknown = (PsdTable)metadata.Data.Entries["Unknown"];
            var nested = (PsdArray)unknown.Entries["Nested"];
            Assert(nested.Items.Length == 2 && nested.Items[0] is PsdArray { Items.Length: 2 } && nested.Items[1] is PsdArray { Items.IsEmpty: true }, "nested and empty arrays retained");
            Assert(((PsdTable)metadata.Content!.Entries[after.Graph.Id]).Entries.ContainsKey("Extra"), "existing intent entry retained by update");
            Assert(text.Contains("hash-format=\"sha512\"", StringComparison.Ordinal), "sha512 selected over sha1");
            Assert(!Directory.EnumerateFileSystemEntries(original).Any(), "observation root untouched");
            var same = plan with { Changes = [], Operation = ContentPlanOperation.Add };
            _ = ContentMaterialization.Apply(observed, stage, same);
            var explicitEntry = (PsdTable)EditorialMetadataReader.Read(stage).Content!.Entries[after.Graph.Id];
            Assert(explicitEntry.Entries.Count == 1 && explicitEntry.Entries["Intent"] is PsdString { Value: "explicit" }, "same-version add replaces requested intent");
            var added = Node("modrinth:collision", "new", null);
            var collision = Path.Combine(root, "mods", "modrinth-collision.pw.toml"); File.WriteAllText(collision, "owned collision");
            var stable = SourceFingerprint.All(SourceTree.Read(root));
            try
            {
                _ = ContentMaterialization.Apply(observed, stage, plan with { Nodes = [after, added], Changes = [change, new(added.Graph.Id, null, added, "dependency")] });
                throw new Exception("Occupied destination accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Content.PathCollision") { }
            Assert(stable == SourceFingerprint.All(SourceTree.Read(root)), "whole batch collision before writes");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { _ = ContentMaterialization.Apply(observed, stage, plan, cancellation: cancellation.Token); throw new Exception("Cancellation ignored."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Core.OperationCancelled") { }
            Assert(stable == SourceFingerprint.All(SourceTree.Read(root)), "pre-cancellation leaves stage unchanged");
            try { _ = ContentMaterialization.Apply(observed, stage, plan, new(MaximumOutputBytes: 1)); throw new Exception("Output quota ignored."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Content.MaterializationLimit") { }
            Assert(stable == SourceFingerprint.All(SourceTree.Read(root)), "combined output quota before writes");
            var invalid = after with { Item = after.Item with { Name = "invalid\ud800" } };
            try { _ = ContentMaterialization.Apply(observed, stage, plan with { Nodes = [invalid], Changes = [change with { After = invalid }] }); throw new Exception("Invalid Unicode accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Content.UnsupportedMetadataEdit") { }
            Assert(stable == SourceFingerprint.All(SourceTree.Read(root)), "invalid Unicode rejected before writes");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ProjectContentNode Node(string id, string version, string? metadata)
    {
        var item = new InventoryItem(id, ContentKind.Mod, "Item", "chosen.jar", "both", ContentSource.Packwiz, version, metadata, true);
        return new(item, new(id, version, item.Name, "mod", "both", true, [], [], []), null,
            new ProviderFile("chosen.jar", "https://example.invalid/$file.jar", true, "sha1-value", "sha512-value", null), true, "transitive");
    }
    private static void Assert(bool value, string label) { if (!value) throw new Exception("Content materialization contract: " + label); }
}
