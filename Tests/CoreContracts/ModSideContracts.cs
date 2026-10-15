using System.IO.Compression;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ModSideContracts
{
    public static async Task VerifyAsync()
    {
        var home = Path.Combine(Path.GetTempPath(), "mpt-side-" + Guid.NewGuid().ToString("N")); var root = Path.Combine(home, "pack with spaces");
        Directory.CreateDirectory(Path.Combine(root, ".modpack")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='side-fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Side\"\nversion=\"1\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"1.21\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "index.toml"), "hash-format=\"sha256\"\nfiles=[]\n");
            Jar("owner", "{\"dependency\":\"*\"}"); Jar("dependency", "{}");
            var path = Path.Combine(root, ".modpack", "metadata.psd1");
            File.WriteAllText(path, "@{Mods=@{'local:mods/owner.jar'=@{Name='Owner Display';Unknown=@{Empty=@();Flag=$true}}};Unknown=@{Empty=@()}}");
            var project = ProjectReader.Read(root); var before = Fingerprint();
            try { _ = await Change("client", "dependency.jar"); throw new Exception("New side conflict accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Compatibility.GraphBlocked") { }
            Assert(Fingerprint() == before, "new graph conflict leaves project unchanged");
            var preview = await Change("host", "owner.jar", dryRun: true);
            Assert(!preview.Applied && preview.Result == "server" && Fingerprint() == before, "host preview has no publication");
            var applied = await Change("host", "owner.jar");
            Assert(applied.Applied && InventoryReader.Read(project).Mods.Single(item => item.Id == "local:mods/owner.jar").Side == "server", "host writes effective local side");
            var data = PsdReader.Parse(File.ReadAllText(path)); var entry = (PsdTable)((PsdTable)data.Entries["Mods"]).Entries["local:mods/owner.jar"];
            Assert(((PsdArray)((PsdTable)entry.Entries["Unknown"]).Entries["Empty"]).Items.IsEmpty && ((PsdString)entry.Entries["Name"]).Value == "Owner Display", "unknown nested empty data and name retained");
            before = Fingerprint();
            try { _ = await Change("both", "owner.jar", failOutput: true); throw new Exception("Output failure accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Transaction.IoFailed" && error.Details == "fixture output preparation fails") { }
            Assert(Fingerprint() == before, "caller output failure leaves side unchanged");
            Task<ModpackTools.Core.Transactions.TransactionResult<string>> Change(string side, string selector, bool dryRun = false, bool failOutput = false) =>
                ModSideOperation.ExecuteAsync(project, new([selector], side, dryRun), Path.Combine(home, "configuration"), Path.Combine(home, "state"), DateTimeOffset.UtcNow,
                    () => throw new Exception("Local side resolved Packwiz."), Environment.ProcessPath!,
                    prepareOutput: failOutput ? (_, _) => throw new IOException("fixture output preparation fails") : null);
            string Fingerprint() => SourceFingerprint.All(SourceTree.Read(root));
            void Jar(string id, string dependencies)
            {
                using var file = File.Create(Path.Combine(root, "mods", id + ".jar")); using var archive = new ZipArchive(file, ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open(), new UTF8Encoding(false));
                writer.Write("{\"schemaVersion\":1,\"id\":\"" + id + "\",\"version\":\"1.0.0\",\"environment\":\"*\",\"depends\":" + dependencies + "}");
            }
        }
        finally { Directory.Delete(home, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Mod side contract: " + message); }
}
