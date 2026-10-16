using System.Net;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Source;

internal static class ProviderCandidateContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools candidates " + Guid.NewGuid().ToString("N"));
        var now = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.Combine(directory, "metadata"));
        try
        {
            var project = new ProviderProject("p1", "Example", "mod", [], "optional", "required");
            var endpoint = project.CandidateEndpoint("1.21", "quilt");
            Assert(endpoint == "project/p1/version?game_versions=%5B%221.21%22%5D&loaders=%5B%22quilt%22%2C%22fabric%22%5D", "exact Quilt query");
            var missing = await Observe(); Assert(missing.Availability == CandidateAvailability.Unknown && missing.Endpoint is null, "missing project precedence");
            Write("project/p1", "{\"id\":\"p1\",\"title\":\"Example\",\"project_type\":\"mod\",\"environment\":[\"client\",\"future\"],\"client_side\":\"optional\",\"server_side\":\"required\"}");
            foreach (var empty in new[] { "[]", "null", " ", "" })
            {
                Write(endpoint, empty); var observation = await Observe();
                Assert(observation.Availability == CandidateAvailability.KnownEmpty && observation.Project!.Environments[1] == "future", "fresh empty and owned environment");
            }
            File.SetLastWriteTimeUtc(PathFor(endpoint), now.UtcDateTime.AddDays(-1));
            Assert((await Observe()).Availability == CandidateAvailability.Unknown, "exact expiration boundary");
            Write(endpoint, "[{\"id\":\"v2\",\"project_id\":\"p1\",\"date_published\":\"2026-10-12T10:00:00Z\"},{\"id\":\"v1\",\"project_id\":\"p1\",\"date_published\":\"2026-10-12T10:00:00Z\"}]");
            var ordered = await Observe(); Assert(ordered.Versions[0].Id == "v1" && ordered.Availability == CandidateAvailability.KnownAvailable, "stable date/id order");
            var bytes = File.ReadAllBytes(PathFor(endpoint));
            await using (var client = new ModrinthVersionClient(directory, now, handler: new ResponseHandler("[{\"id\":\"v3\",\"project_id\":\"p1\",\"date_published\":\"bad\"}]")))
            {
                try { await ProviderCandidates.ObserveAsync(directory, "p1", "1.21", "quilt", now, client, project); throw new InvalidOperationException("Expected invalid date."); }
                catch (DomainException error) { Assert(error.Id == "ModpackTools.Metadata.InvalidCandidateDate", "date domain error"); }
            }
            Assert(bytes.SequenceEqual(File.ReadAllBytes(PathFor(endpoint))), "bad checked date preserves cache");
            await using (var client = new ModrinthVersionClient(directory, now, handler: new ResponseHandler("{\"id\":\"v3\",\"project_id\":\"p1\",\"date_published\":\"2026-10-12T10:00:00Z\"}")))
            {
                try { await client.GetCandidateVersionsAsync(project, "1.21", "quilt"); throw new InvalidOperationException("Expected malformed list."); }
                catch (DomainException error) { Assert(error.Id == "ModpackTools.Metadata.InvalidProviderResponse", "object list rejected"); }
            }
            Assert(bytes.SequenceEqual(File.ReadAllBytes(PathFor(endpoint))), "malformed checked list preserves cache");
            await using (var client = new ModrinthVersionClient(directory, now, handler: new ResponseHandler("[]")))
                Assert((await ProviderCandidates.ObserveAsync(directory, "p1", "1.21", "quilt", now, client, project)).Availability == CandidateAvailability.KnownEmpty, "checked empty");
            Assert((project with { RawKind = "shader" }).CandidateEndpoint("1.21", "quilt") == "project/p1/version?game_versions=%5B%221.21%22%5D", "shader E43");
            Assert((project with { RawKind = "resourcepack" }).CandidateEndpoint("1.21", "quilt").EndsWith("&loaders=%5B%22minecraft%22%5D", StringComparison.Ordinal), "resource query");
            Assert(!Directory.EnumerateFiles(Path.Combine(directory, "metadata"), "*.tmp").Any(), "owned temporary cleanup");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Task<ProviderCandidateObservation> Observe() => ProviderCandidates.ObserveAsync(directory, "p1", "1.21", "quilt", now);
        string PathFor(string endpoint) => Path.Combine(directory, "metadata", TextHash.Compute(endpoint) + ".json");
        void Write(string endpoint, string value) { File.WriteAllText(PathFor(endpoint), value, new UTF8Encoding(false)); File.SetLastWriteTimeUtc(PathFor(endpoint), now.UtcDateTime); }
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Provider candidates: " + message); }
    }
    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
