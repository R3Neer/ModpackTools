using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core;
using ModpackTools.Core.Source;

internal static class SourceTreeContracts
{
    public static void Verify()
    {
        var directory = Path.Combine(Path.GetTempPath(), "modpacktools-source-contracts-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "dist"));
            Directory.CreateDirectory(Path.Combine(directory, ".git"));
            File.WriteAllText(Path.Combine(directory, ".git", "ignored"), "ignored");
            File.WriteAllText(Path.Combine(directory, "a"), "AAA");
            File.WriteAllText(Path.Combine(directory, "dist", "artifact"), "OLD");
            var before = SourceTree.Read(directory);
            var health = SourceFingerprint.Health(before, CultureInfo.GetCultureInfo("en-US"));
            var all = SourceFingerprint.All(before);
            var healthOnly = SourceTree.ReadForHealth(directory, new(MaximumBytes: 3));
            if (healthOnly.Files.Count != 1 || SourceFingerprint.Health(healthOnly, CultureInfo.GetCultureInfo("en-US")) != health)
                throw new InvalidOperationException("Health reader hashed dist before excluding it.");
            var time = File.GetLastWriteTimeUtc(Path.Combine(directory, "dist", "artifact"));
            File.WriteAllText(Path.Combine(directory, "dist", "artifact"), "NEW");
            File.SetLastWriteTimeUtc(Path.Combine(directory, "dist", "artifact"), time);
            var after = SourceTree.Read(directory);
            if (before.Files.Count != 2 || before.Files.Keys.Any(path => path.StartsWith(".git/", StringComparison.Ordinal))
                || SourceFingerprint.Health(after, CultureInfo.GetCultureInfo("en-US")) != health || SourceFingerprint.All(after) == all
                || SourceTreeChanges.Compare(before, after).Single().Path != "dist/artifact")
                throw new InvalidOperationException("Source snapshot lost protected dist, content hashes or health scope.");
            Directory.CreateDirectory(Path.Combine(directory, "d\u00ADist")); File.WriteAllText(Path.Combine(directory, "d\u00ADist", "kept"), "A");
            if (!SourceTree.ReadForHealth(directory).Files.ContainsKey("d\u00ADist/kept"))
                throw new InvalidOperationException("Health scope used cultural equivalence for distinct physical paths.");
            var exclusive = new FileStream(Path.Combine(directory, "a"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using (exclusive) Expect(() => SourceTree.Read(directory), "Source.TreeReadFailed");
            Expect(() => SourceTree.Read(directory, new(MaximumBytes: 5)), "Source.TreeReadLimitExceeded");
            Expect(() => SourceTree.Read(Path.Combine(directory, "absent"), new(MaximumFiles: 0)), "Source.InvalidTreeReadPolicy");
            var left = ImmutableDictionary.Create<string, string?>(StringComparer.OrdinalIgnoreCase).Add("é.txt", "A").Add("e\u0301.txt", "A");
            var right = left.SetItem("e\u0301.txt", "B");
            if (SourceTreeChanges.Compare(left, right).Single().Path != "e\u0301.txt")
                throw new InvalidOperationException("Cultural path ties still hide a source change.");
            var sensitive = ImmutableDictionary.Create<string, string?>(StringComparer.Ordinal).Add("A", "same").Add("a", "same");
            try { SourceTreeChanges.Compare(sensitive, right); }
            catch (DomainException exception) when (exception.Id == "ModpackTools.Transaction.PathCollision") { return; }
            throw new InvalidOperationException("Caller hash-map case collisions were silently folded.");
        }
        finally
        {
            var absolute = Path.GetFullPath(directory);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("modpacktools-source-contracts-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe source fixture cleanup.");
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
        }
    }
    private static void Expect(Func<SourceTreeSnapshot> action, string id)
    {
        try { action(); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return; }
        throw new InvalidOperationException("Expected source reader diagnostic " + id);
    }
}
