using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class BatchSelectorContracts
{
    public static void Verify()
    {
        var now = DateTimeOffset.UtcNow;
        var input = ImmutableArray.Create("Alpha", "ALPHA", "é", "e\u0301", "ﬃ", "ffi", "soft\u00adhyphen", "softhyphen", " ", "01");
        var expected = input.RemoveAt(1);
        if (!BatchSelectorResolver.Resolve(input, null, "fixture", now).SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("Literal batch dedup changed comparison, order or spelling.");
        if (!BatchSelectorResolver.Resolve([], null, "fixture", now).IsEmpty)
            throw new InvalidOperationException("Empty helper batch no longer succeeds.");
        foreach (var invalid in new ImmutableArray<string>[] { default, [""], [null!], ["1", ""], ImmutableArray.CreateRange(Enumerable.Repeat("x", 1025)) })
        {
            try { BatchSelectorResolver.Resolve(invalid, null, "fixture", now); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Batch input grammar did not precede reference lookup.");
        }
        if (BatchSelectorResolver.Resolve(ImmutableArray.CreateRange(Enumerable.Repeat("First", 1024)), null, "fixture", now).Length != 1)
            throw new InvalidOperationException("Exact batch count boundary failed.");
        VerifyOwnedSnapshot();
    }

    private static void VerifyOwnedSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "modpacktools-batch-contracts-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "last-inventory.json");
        Directory.CreateDirectory(directory);
        try
        {
            var clock = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
            const string original = "{\"SchemaVersion\":1,\"CreatedUtc\":\"2026-10-13T12:00:00Z\",\"ProjectId\":\"fixture\",\"Results\":[{\"Index\":1,\"Kind\":\"mod\",\"Selector\":\"Original\"},{\"Index\":2,\"Kind\":\"mod\",\"Selector\":null}]}";
            File.WriteAllText(path, original);
            var snapshot = ReferenceCacheReader.Read(directory);
            File.WriteAllText(path, original.Replace("Original", "Replaced", StringComparison.Ordinal));
            if (!BatchSelectorResolver.Resolve(["1", "1", "2", "original"], snapshot, "fixture", clock).SequenceEqual(["Original", ""], StringComparer.Ordinal))
                throw new InvalidOperationException("Owned snapshot or empty mapped selector changed during replacement.");
            if (!BatchSelectorResolver.ResolveFromDirectory(["1"], directory, "fixture", clock).SequenceEqual(["Replaced"], StringComparer.Ordinal))
                throw new InvalidOperationException("New batch did not read the new cache generation.");
            File.Delete(path);
            if (!BatchSelectorResolver.Resolve(["1"], snapshot, "fixture", clock.AddHours(24)).SequenceEqual(["Original"], StringComparer.Ordinal))
                throw new InvalidOperationException("Owned snapshot required a live file or lost the exact 24-hour boundary.");
            Expect(() => BatchSelectorResolver.Resolve(["1"], snapshot, "fixture", clock.AddHours(24).AddTicks(1)), "Inventory.ReferenceCacheExpired");
            Expect(() => BatchSelectorResolver.ResolveFromDirectory(["1"], directory, "fixture", clock), "Inventory.ReferenceCacheNotFound");
            File.WriteAllText(path, original.Replace("\"mod\"", "\"resourcepack\"", StringComparison.Ordinal));
            snapshot = ReferenceCacheReader.Read(directory);
            Expect(() => BatchSelectorResolver.Resolve(["1"], snapshot, "fixture", clock), "Inventory.ReferenceKindMismatch");
            if (BatchSelectorResolver.Resolve(["1"], snapshot, "fixture", clock, ReferenceLookupPolicy.Default)[0] != "Original")
                throw new InvalidOperationException("Explicit all-kinds policy was confused with batch default mods.");
        }
        finally
        {
            var absolute = Path.GetFullPath(directory);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("modpacktools-batch-contracts-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe batch fixture cleanup.");
            Directory.Delete(absolute, true);
        }
    }

    private static void Expect(Func<ImmutableArray<string>> action, string id)
    {
        try { action(); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return; }
        throw new InvalidOperationException("Expected batch error " + id);
    }
}
