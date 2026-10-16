using ModpackTools.Core;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Source;

internal static class ProviderCacheContracts
{
    public static void Verify()
    {
        var root = Path.Combine(Path.GetTempPath(), "modpack-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "metadata"));
        var path = Path.Combine(root, "metadata", TextHash.Compute("version/v1") + ".json");
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        try
        {
            Expect(ProviderCacheStatus.Missing);
            Write("{\"id\":\"v1\",\"project_id\":123,\"date_published\":\"2024-01-02T03:04:05Z\",\"files\":[{\"filename\":\"a.jar\",\"primary\":true}],\"dependencies\":[{\"dependency_type\":\"required\",\"version_id\":false}],\"loaders\":[\"fabric\"]}");
            var observation = Expect(ProviderCacheStatus.Fresh);
            if (observation.Version is not { } version || version.ProjectId != "123" || version.Published.Text != "01/02/2024 03:04:05"
                || version.Published.Value?.Kind != DateTimeKind.Utc || version.Published.Instant?.Offset != TimeSpan.Zero
                || !version.Files[0].Primary || version.Dependencies[0].VersionId != "False") throw new InvalidOperationException("Provider scalar/date ownership changed.");
            var bytes = File.ReadAllBytes(path);
            try { ProviderVersionCache.Read(root, "v1", now, new(MaximumBytes: bytes.Length - 1)); throw new InvalidOperationException("Provider bytes unbounded."); }
            catch (DomainException exception) when (exception.Id == "ModpackTools.Metadata.ProviderCacheLimit") { }
            _ = ProviderVersionCache.Read(root, "v1", now, new(MaximumBytes: bytes.Length));
            File.SetLastWriteTimeUtc(path, now.UtcDateTime.AddDays(-1)); Expect(ProviderCacheStatus.Expired);
            Write("{invalid"); File.SetLastWriteTimeUtc(path, now.UtcDateTime.AddDays(-1)); Expect(ProviderCacheStatus.Expired);
            File.SetLastWriteTimeUtc(path, now.UtcDateTime); Expect(ProviderCacheStatus.Unreadable);
            Write("null"); Expect(ProviderCacheStatus.Empty);
            Write("[]"); Expect(ProviderCacheStatus.Empty);
            Write("[{},{}]"); Expect(ProviderCacheStatus.Unreadable);
            Write("{}"); Expect(ProviderCacheStatus.Unreadable);
            Write("[{\"ID\":null}]"); if (Expect(ProviderCacheStatus.Fresh).Version?.Id != "") throw new InvalidOperationException("Nullable provider ID changed.");
            Write("{\"id\":\"v1\",\"files\":[{\"size\":0},{\"size\":9223372036854775807}]}");
            var sizes = Expect(ProviderCacheStatus.Fresh).Version!.Files;
            if (sizes[0].Size != 0 || sizes[1].Size != long.MaxValue) throw new InvalidOperationException("Provider file sizes lost Int64 precision.");
            Write("{\"id\":\"v1\",\"files\":[{\"size\":1.5}]}"); Expect(ProviderCacheStatus.Unreadable);
            Write("{\"id\":\"v1\",\"files\":[{\"primary\":\"Tr\\u00adue\"},{\"primary\":\"True\\u0000\"}]}");
            if (Expect(ProviderCacheStatus.Fresh).Version!.Files.Any(file => !file.Primary)) throw new InvalidOperationException("Provider Boolean text comparison changed.");
            try { ProviderVersionCache.Read(root, "none", now, new(MaximumNodes: 0)); throw new InvalidOperationException("Provider policy not validated upfront."); }
            catch (DomainException exception) when (exception.Id == "ModpackTools.Metadata.InvalidProviderCachePolicy") { }
        }
        finally { Directory.Delete(root, recursive: true); }
        void Write(string text) { File.WriteAllText(path, text); File.SetLastWriteTimeUtc(path, now.UtcDateTime); }
        ProviderCacheObservation Expect(ProviderCacheStatus expected)
        {
            var result = ProviderVersionCache.Read(root, "v1", now);
            if (result.Status != expected || expected != ProviderCacheStatus.Fresh && result.Version is not null)
                throw new InvalidOperationException("Provider availability changed: " + expected);
            return result;
        }
    }
}
