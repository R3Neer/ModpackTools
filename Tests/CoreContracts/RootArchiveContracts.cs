using System.IO.Compression;
using System.Text;
using ModpackTools.Core.Artifacts;

internal static class RootArchiveContracts
{
    public static void Verify()
    {
        var directory = Path.Combine(Path.GetTempPath(), "modpacktools-root-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.jar");
        try
        {
            const string fabric = "{schemaVersion:1,id:'m',version:'1'}";
            Make(path, [("fabric.mod.json", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(fabric)).ToArray())]);
            var decoded = RootArchiveReader.Read(path, "fabric");
            if (decoded.Status != ArchiveReadStatus.Read || decoded.Root.Mods is not [{ Id: "m" }]) throw new InvalidOperationException("Root archive BOM decoding changed.");
            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Make(path, [("fabric.mod.json", []), ("fabric.mod.json", Encoding.UTF8.GetBytes(fabric))]);
            if (RootArchiveReader.Read(path, "fabric").Root.Status != RootManifestStatus.Missing) throw new InvalidOperationException("Root duplicate selection changed.");
            Make(path, [("META-INF/mods.toml", Encoding.UTF8.GetBytes("broken=")), ("META-INF/MANIFEST.MF", new byte[65])]);
            var stage = RootArchiveReader.Read(path, "forge", new() { Archive = new(MaximumMetadataBytes: 64) });
            if (stage.Root.Diagnostics is not [{ Code: "ManifestIncomplete" }]) throw new InvalidOperationException("MF read preceded TOML validation.");
            Make(path, [("META-INF/mods.toml", Encoding.UTF8.GetBytes("[[mods]]\nmodId='m'\nversion='1'")), ("META-INF/jarjar/metadata.json", new byte[65])]);
            var prefix = RootArchiveReader.Read(path, "forge", new() { Archive = new(MaximumMetadataBytes: 64) });
            if (prefix.Status != ArchiveReadStatus.Limited || prefix.Root.Mods.Length != 1 || prefix.Root.CanTraverseNested) throw new InvalidOperationException("Late archive limit erased root prefix.");
            File.WriteAllText(path, "notzip");
            if (RootArchiveReader.Read(path, "fabric").Status != ArchiveReadStatus.Unreadable) throw new InvalidOperationException("Unreadable archive became missing metadata.");
            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally
        {
            var full = Path.GetFullPath(directory);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("modpacktools-root-archive-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe archive fixture cleanup.");
            Directory.Delete(full, true);
        }
    }
    private static void Make(string path, (string Name, byte[] Bytes)[] entries)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var entry in entries) { using var stream = archive.CreateEntry(entry.Name).Open(); stream.Write(entry.Bytes); }
    }
}
