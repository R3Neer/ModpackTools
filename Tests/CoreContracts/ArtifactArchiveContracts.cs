using System.IO.Compression;
using System.Text;
using ModpackTools.Core.Artifacts;

internal static class ArtifactArchiveContracts
{
    public static void Verify()
    {
        var path = Path.Combine(Path.GetTempPath(), "modpacktools-artifact-" + Guid.NewGuid().ToString("N") + ".jar");
        try
        {
            var child = Zip([("fabric.mod.json", Text("{schemaVersion:1,id:'c',version:'1'}"))]);
            File.WriteAllBytes(path, Zip([("fabric.mod.json", Text("{schemaVersion:1,id:'r',version:'1',jars:[{file:'c.jar'},{file:'missing'},{file:'c.jar'}]}")), ("c.jar", child)]));
            var read = ArtifactArchiveReader.Read(path, "fabric");
            if (!read.Mods.Select(mod => mod.Declaration.Id).SequenceEqual(["r", "c", "c"])
                || read.Mods[0].Nested || !read.Mods[1].Nested || !read.Mods[2].EntryChain.SequenceEqual(["c.jar"])
                || read.Diagnostics is not [{ Diagnostic.Code: "NestedUnavailable" }]) throw new InvalidOperationException("Nested order, duplicates or provenance changed.");
            var limited = ArtifactArchiveReader.Read(path, "fabric", new() { MaximumNestedAttempts = 1 });
            if (limited.Status != ArtifactTraversalStatus.Limited || limited.Mods.Length != 2
                || limited.Diagnostics.Count(item => item.Diagnostic.Code == "TraversalLimit") != 1) throw new InvalidOperationException("Global attempt limit did not retain its prefix.");
            File.WriteAllBytes(path, Zip([("fabric.mod.json", Text("{schemaVersion:1,id:'r',version:'1',jars:[{file:'bad'},{file:'c'}]}")), ("bad", Text("notzip")), ("c", child)]));
            if (ArtifactArchiveReader.Read(path, "fabric").Mods.Length != 1) throw new InvalidOperationException("Corrupt child did not stop current siblings.");
            File.WriteAllBytes(path, Zip([("fabric.mod.json", Text("{schemaVersion:1,id:'r',version:'1',jars:[{file:'bad'},{file:'c'}]}")), ("bad", Zip([("fabric.mod.json", Text("invalid"))])), ("c", child)]));
            if (ArtifactArchiveReader.Read(path, "fabric").Mods.Length != 2) throw new InvalidOperationException("Child declaration failure stopped parent siblings.");
            File.WriteAllBytes(path, Zip([("fabric.mod.json", Text("{schemaVersion:1,id:'r',version:'1',jars:[{file:'empty'}]}")), ("empty", Zip([]))]));
            var exact = ArtifactArchiveReader.Read(path, "fabric", new() { MaximumMods = 1, MaximumDiagnostics = 1, MaximumEntries = 2 });
            if (exact.Status != ArtifactTraversalStatus.Complete || exact.Diagnostics.Length != 0) throw new InvalidOperationException("Zero internal mod allowance rejected an empty child.");
            byte[] chain = Zip([("fabric.mod.json", Text("{schemaVersion:1,id:'m17',version:'1'}"))]);
            for (var depth = 16; depth >= 0; depth--)
                chain = Zip([("fabric.mod.json", Text("{schemaVersion:1,id:'m" + depth + "',version:'1',jars:[{file:'next'}]}")), ("next", chain)]);
            File.WriteAllBytes(path, chain);
            var deep = ArtifactArchiveReader.Read(path, "fabric");
            if (deep.Mods.Length != 17 || deep.Diagnostics is not [{ Diagnostic.Code: "NestedDepth" }]) throw new InvalidOperationException("Depth17 priority or cutoff changed.");
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] Zip((string Name, byte[] Bytes)[] entries)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            foreach (var entry in entries) { using var stream = archive.CreateEntry(entry.Name).Open(); stream.Write(entry.Bytes); }
        return bytes.ToArray();
    }
}
