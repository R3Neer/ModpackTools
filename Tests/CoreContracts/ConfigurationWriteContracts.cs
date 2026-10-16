using System.Security.Cryptography;
using ModpackTools.Core;
using ModpackTools.Core.Data;

internal static class ConfigurationWriteContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpt-config-write-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "configuration with spaces"); Directory.CreateDirectory(config);
            var root = Path.Combine(directory, "catalog with spaces"); Directory.CreateDirectory(root);
            var path = Path.Combine(config, "config.psd1");
            File.WriteAllText(path, "@{Root='old';PackwizPath='old-tool';Unknown=@{Empty=@();Rows=@('a','b');Flag=$true}}");
            var before = File.ReadAllBytes(path);
            try
            {
                _ = await ConfigurationStore.SetAsync(config, new(ConfigurationSetting.Root, root), prepareOutput: (_, _) => throw new IOException("fixture output failure"));
                throw new Exception("Output failure accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Configuration.WriteFailed" && error.Details == "fixture output failure") { }
            Assert(File.ReadAllBytes(path).SequenceEqual(before), "output failure leaves original bytes");
            const string external = "@{Root='external';Unknown='keep external edit'}";
            try
            {
                _ = await ConfigurationStore.SetAsync(config, new(ConfigurationSetting.Root, root), prepareOutput: (_, _) =>
                { File.WriteAllText(path, external); return ValueTask.CompletedTask; });
                throw new Exception("External edit replaced.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Configuration.Changed") { }
            Assert(File.ReadAllText(path) == external && !Directory.EnumerateFiles(config, ".config-*.tmp").Any(), "external edit wins and owned temporary is removed");
            File.WriteAllBytes(path, before);
            var result = await ConfigurationStore.SetAsync(config, new(ConfigurationSetting.Root, root));
            var data = PsdReader.Parse(File.ReadAllText(path));
            Assert(result.Value == root && result.Hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) && data.Entries["PackwizPath"] is PsdString { Value: "old-tool" }, "root write has verified bytes and retains configured tool");
            Assert(((PsdArray)((PsdTable)data.Entries["Unknown"]).Entries["Empty"]).Items.IsEmpty, "unknown empty data retained");
            _ = await ConfigurationStore.SetAsync(config, new(ConfigurationSetting.Packwiz, "AUTO"));
            data = PsdReader.Parse(File.ReadAllText(path));
            Assert(!data.Entries.ContainsKey("PackwizPath") && data.Entries["Root"] is PsdString value && value.Value == root, "auto removes only explicit tool");
            File.WriteAllBytes(path, [0xff, 0xff]);
            try { _ = await ConfigurationStore.SetAsync(config, new(ConfigurationSetting.Root, Path.Combine(directory, "missing-root"))); throw new Exception("Invalid UTF8 accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Configuration.InvalidFile") { }
            Assert(File.ReadAllBytes(path).SequenceEqual(new byte[] { 0xff, 0xff }), "invalid UTF8 receives typed data failure before value validation and remains intact");
            try { _ = ConfigurationReader.ReadRoot(config); throw new Exception("Reader accepted invalid UTF8."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Configuration.InvalidFile") { }
            var missingConfig = Path.Combine(directory, "not-created-config");
            try { _ = await ConfigurationStore.SetAsync(missingConfig, new(ConfigurationSetting.Root, Path.Combine(directory, "missing-root"))); throw new Exception("Missing root accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Configuration.InvalidRoot") { }
            Assert(!Directory.Exists(missingConfig), "invalid value does not create a configuration directory");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Configuration write contract: " + message); }
}
