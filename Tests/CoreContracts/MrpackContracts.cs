using System.IO.Compression;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Build;

internal static class MrpackContracts
{
    public static void Verify()
    {
        var home = Path.Combine(Path.GetTempPath(), "mpt-mrpack-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
        try
        {
            const string first = """{"name":"Fixture","versionId":"1","dependencies":{"minecraft":"1.21"},"files":[{"path":"mods/a.jar","hashes":{"sha1":"111","sha512":"222"},"downloads":["https://example.invalid/b","https://example.invalid/a"],"env":{"server":"required","client":"optional"},"fileSize":12}]}""";
            const string reordered = """{"versionId":"1","files":[{"fileSize":12,"env":{"client":"optional","server":"required"},"downloads":["https://example.invalid/a","https://example.invalid/b"],"hashes":{"sha512":"222","sha1":"111"},"path":"mods/a.jar"}],"dependencies":{"minecraft":"1.21"},"name":"Fixture"}""";
            var oldPath = Write("old.mrpack", first); var reorderedPath = Write("reordered.mrpack", reordered, reverse: true);
            var baseline = MrpackSnapshot.Read(oldPath); var current = MrpackSnapshot.Read(reorderedPath);
            Assert(baseline.Length == 6 && MrpackSnapshot.Compare(baseline, current).Total == 0, "JSON/download/ZIP ordering does not change semantic content");
            foreach (var encoding in new Encoding[] { new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UTF32Encoding(false, true, true) })
            {
                var encoded = Write("bom-" + encoding.WebName + ".mrpack", first, manifestEncoding: encoding);
                Assert(MrpackSnapshot.Compare(baseline, MrpackSnapshot.Read(encoded)).Total == 0, "BOM detection preserves original manifest text encoding");
            }
            Assert(MrpackSnapshot.Compare([new("pack:name", "PACK", "name", "é")], [new("pack:name", "PACK", "name", "e\u0301")]).Total == 0, "text fingerprint comparison preserves PowerShell invariant case-sensitive semantics");
            var changedPath = Write("changed.mrpack", reordered.Replace("\"versionId\":\"1\"", "\"versionId\":\"2\"", StringComparison.Ordinal), config: "changed", extra: true);
            var difference = MrpackSnapshot.Compare(baseline, MrpackSnapshot.Read(changedPath));
            Assert(difference.Total == 3 && difference.Added.Single().Kind == "ARCHIVE" && difference.Changed.Length == 2 && difference.Removed.IsEmpty, "pack/config changes and additional archive contents are visible");
            var duplicate = Write("duplicate.mrpack", first, duplicate: true);
            try { _ = MrpackSnapshot.Read(duplicate); throw new Exception("Duplicate ZIP identity accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Diff.InvalidArtifact") { }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { _ = MrpackSnapshot.Compare(baseline, current, cancellation: cancellation.Token); throw new Exception("Cancelled comparison accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Core.OperationCancelled") { }
            string Write(string name, string manifest, bool reverse = false, string config = "same", bool extra = false, bool duplicate = false, Encoding? manifestEncoding = null)
            {
                var path = Path.Combine(home, name); using var file = File.Create(path); using var archive = new ZipArchive(file, ZipArchiveMode.Create);
                if (reverse) Entry("overrides/config/a.txt", config);
                Entry("modrinth.index.json", manifest); Entry("LICENSE.txt", "owned license");
                if (!reverse) Entry("overrides/config/a.txt", config);
                if (extra) Entry("server-overrides/config/extra.txt", "extra");
                if (duplicate) Entry("overrides/CONFIG/A.TXT", "duplicate");
                return path;
                void Entry(string entry, string value) { using var writer = new StreamWriter(archive.CreateEntry(entry).Open(), entry == "modrinth.index.json" ? manifestEncoding ?? new UTF8Encoding(false) : new UTF8Encoding(false)); writer.Write(value); }
            }
        }
        finally { Directory.Delete(home, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("MRPack contract: " + message); }
}
