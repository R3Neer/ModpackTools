// Evaluation fixture only: no ModpackTools functionality.
if (args.Length == 2 && args[0] == "--parsers")
{
    var original = File.ReadAllText(args[1]);
    var document = Tomlyn.Toml.Parse(original);
    if (document.HasErrors || document.ToString() != original || ModpackTools.MavenVersion.Compare("1.0-alpha", "1.0") >= 0)
        throw new InvalidOperationException("Parser/comparator probe failed");
    Console.WriteLine("{\"ok\":true,\"benchmark\":\"parser-viability\"}");
}
else if (args.Length == 2 && args[0] == "--hash")
{
    var files = new List<string>();
    void Walk(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (Path.GetFileName(entry) == ".git") continue;
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Diagnostic workload must not contain links/placeholders");
            if (Directory.Exists(entry)) Walk(entry); else files.Add(entry);
        }
    }
    Walk(args[1]);
    files.Sort((a, b) => StringComparer.Ordinal.Compare(Path.GetRelativePath(args[1], a).Replace('\\', '/'), Path.GetRelativePath(args[1], b).Replace('\\', '/')));
    long bytes = 0;
    using var fingerprint = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
    foreach (var file in files)
    {
        using var stream = File.OpenRead(file);
        bytes += stream.Length;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        fingerprint.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(args[1], file).Replace('\\', '/') + ":" + hash + "\n"));
    }
    Console.WriteLine($"{{\"ok\":true,\"benchmark\":\"tree-hash-only\",\"files\":{files.Count},\"bytes\":{bytes},\"fingerprint\":\"{Convert.ToHexString(fingerprint.GetHashAndReset()).ToLowerInvariant()}\"}}");
}
else Console.WriteLine("{\"ok\":true,\"benchmark\":\"startup-only\"}");
