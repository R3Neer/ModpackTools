using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ProjectLookupContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools project lookup " + Guid.NewGuid().ToString("N"));
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        const string body = """{"id":"p1","title":"Resources","project_type":"resourcepack","extra":{"owned":42}}""";
        try
        {
            var handler = new ProviderAcquisitionContracts.Handler((_, _) => Task.FromResult(ProviderAcquisitionContracts.Response(body)));
            await using (var client = new ModrinthVersionClient(directory, now, handler: handler))
            {
                var found = await client.LookupProjectAsync("pretty-slug");
                if (found.Id != "p1" || found.Kind != ProviderContentKind.ResourcePack) throw new Exception("Alias canonical identity changed.");
                _ = await client.LookupProjectAsync("pretty-slug");
                if (handler.Calls != 1) throw new Exception("Alias endpoint was not memoized.");
                var canonical = Path.Combine(directory, "metadata", TextHash.Compute("project/p1") + ".json");
                using var cache = JsonDocument.Parse(File.ReadAllText(canonical));
                if (cache.RootElement.GetProperty("extra").GetProperty("owned").GetInt32() != 42 || File.GetLastWriteTimeUtc(canonical) != now.UtcDateTime)
                    throw new Exception("Canonical alias cache lost owned wire or timestamp.");
                if (File.Exists(Path.Combine(directory, "metadata", TextHash.Compute("project/pretty-slug") + ".json"))) throw new Exception("Alias-keyed project cache was written.");
                _ = await client.LookupProjectAsync("alias");
                try { _ = await client.GetProjectAsync("alias"); throw new Exception("Alias memo bypassed canonical identity validation."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Metadata.InvalidProviderResponse") { }
                if (handler.Calls != 3) throw new Exception("Canonical and lookup contracts were not isolated.");
                foreach (var bad in new[] { "", "../p1", "p1?x=1", "https://modrinth.com/mod/p1", new string('a', 257), "é" })
                {
                    try { _ = await client.LookupProjectAsync(bad); throw new Exception("Invalid selector reached HTTP."); }
                    catch (DomainException error) when (error.Id == "ModpackTools.Metadata.InvalidProjectSelector") { }
                }
                if (handler.Calls != 3) throw new Exception("Selector validation ran after HTTP.");
                foreach (var slug in new[] { "foo.bar", "foo+bar", "foo'bar", "résumé" })
                    if ((await client.LookupProjectAsync(slug)).Id != "p1") throw new Exception("Supported provider slug was rejected.");
            }
            var project = new ProjectContext("fixture", directory, null, null, "fixture.mrpack", "1.21.1", "quilt", "0.1", "index.toml", Path.Combine(directory, "index.toml"));
            var calls = new List<string>();
            var sourceHandler = new ProviderAcquisitionContracts.Handler((request, _) =>
            {
                var path = request.RequestUri!.PathAndQuery; calls.Add(path);
                return Task.FromResult(ProviderAcquisitionContracts.Response(path.Contains("/version?", StringComparison.Ordinal) ? "[]" : body));
            });
            await using (var source = await ResolutionAcquisition.FromObservedNodesAsync(project, [], Path.Combine(directory, "source"), now, providerHandler: sourceHandler))
            {
                var info = await source.LookupProjectAsync("pretty-slug");
                var domain = await source.GetCandidatesAsync("modrinth:" + info.Id, default);
                if (domain.Availability != CandidateAvailability.KnownEmpty || calls.Count != 2 || calls.Any(path => path == "/v2/project/p1"))
                    throw new Exception("Owned alias metadata was fetched again for candidates.");
                if (source.ArtifactStatistics.Requests != 0) throw new Exception("Identification downloaded artifacts.");
            }
            var reverse = new ProviderAcquisitionContracts.Handler((_, _) => Task.FromResult(ProviderAcquisitionContracts.Response(body)));
            await using (var client = new ModrinthVersionClient(Path.Combine(directory, "reverse"), now, handler: reverse))
            {
                try { _ = await client.GetProjectAsync("alias"); throw new Exception("Canonical mismatch accepted."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Metadata.InvalidProviderResponse") { }
                if ((await client.LookupProjectAsync("alias")).Id != "p1" || reverse.Calls != 2) throw new Exception("Canonical failure poisoned alias lookup.");
            }
            await using (var client = new ModrinthVersionClient(Path.Combine(directory, "invalid"), now,
                handler: new ProviderAcquisitionContracts.Handler((_, _) => Task.FromResult(ProviderAcquisitionContracts.Response("{\"id\":\"../bad\"}")))))
            {
                try { _ = await client.LookupProjectAsync("slug"); throw new Exception("Malformed returned identity accepted."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Metadata.InvalidProjectId") { }
                if (Directory.EnumerateFiles(Path.Combine(directory, "invalid", "metadata")).Any()) throw new Exception("Invalid identity persisted metadata.");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
