using System.Net;
using System.Text;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;

internal static class CompatibleVersionContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools version listing " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = new ProjectContext("fixture", directory, "Fixture", "1", "pack.mrpack", "1.21", "quilt", "0.1", "index.toml", Path.Combine(directory, "index.toml"));
            var item = new InventoryItem("modrinth:p1", ContentKind.Mod, "Example", "old.jar", "both", ContentSource.Packwiz, "V1", "example.pw.toml", false);
            const string body = """
                [
                  {"id":"v2","project_id":"p1","name":"Second","version_number":"2","version_type":"release","date_published":"2026-01-01T00:00:00Z","files":[{"filename":"a.jar","primary":false},{"filename":"b.jar","primary":"false"}],"loaders":["quilt"],"game_versions":["1.21"],"dependencies":[]},
                  {"id":"v1","project_id":"p1","name":"First","version_number":"1","version_type":"beta","date_published":"2026-01-01T00:00:00Z","files":[{"filename":"one.jar","primary":2}],"loaders":[],"game_versions":[],"dependencies":[]},
                  {"id":"v0","project_id":"p1","name":"Undated","version_number":"0","version_type":"alpha","date_published":null,"files":[],"loaders":[],"game_versions":[],"dependencies":null}
                ]
                """;
            var now = DateTimeOffset.UtcNow;
            var handler = new BodyHandler(body);
            var view = await CompatibleVersions.ReadAsync(project, item, directory, now, handler: handler);
            Assert(view.Versions.Select(row => row.Candidate.Version.Id).SequenceEqual(new[] { "v2", "v1", "v0" }), "stable publication ties and null date");
            Assert(view.Versions[0].Candidate.Filename == "b.jar" && view.Versions[1].Installed && view.Versions[2].Candidate.Filename is null, "truthy primary, installed case and no files");
            Assert(handler.Endpoint == "/v2/project/p1/version?game_versions=%5B%221.21%22%5D&loaders=%5B%22quilt%22%5D&include_changelog=false", "public Quilt endpoint differs from solver candidates");
            var path = Path.Combine(directory, "last-versions.json"); var previous = File.ReadAllText(path);
            using (var cache = JsonDocument.Parse(previous))
            { Assert(cache.RootElement.GetProperty("Versions")[2].GetProperty("Dependencies")[0].ValueKind == JsonValueKind.Null, "null dependency occurrence preserved"); }
            Assert(!Directory.EnumerateFiles(Path.Combine(directory, "metadata")).Any(), "listing does not publish provider metadata caches or leak temporaries");
            await Fails("ModpackTools.Metadata.InvalidProviderResponse", () => CompatibleVersions.ReadAsync(project, item, directory, now,
                handler: new BodyHandler(body.Replace("\"date_published\":null,", "", StringComparison.Ordinal))));
            await Fails("ModpackTools.Metadata.ProviderRequestLimit", () => CompatibleVersions.ReadAsync(project, item, directory, now,
                requests: new(MaximumListingWireBytes: 1), handler: new BodyHandler(body)));
            await Fails("ModpackTools.Versions.CacheWriteLimit", () => CompatibleVersions.ReadAsync(project, item, directory, now,
                cache: new(1), handler: new BodyHandler(body)));
            Assert(File.ReadAllText(path) == previous, "failed projection or atomic cache write preserves old cache");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Fails("ModpackTools.Core.OperationCancelled", () => CompatibleVersions.ReadAsync(project, item, directory, now,
                cancellation: cancellation.Token, handler: new BodyHandler(body)));
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Fails(string id, Func<Task<CompatibleVersionView>> action)
    {
        try { await action(); throw new InvalidOperationException("Expected " + id); }
        catch (DomainException exception) when (exception.Id == id) { }
    }
    private static void Assert(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("Compatible version contract failed: " + label); }
    private sealed class BodyHandler(string body) : HttpMessageHandler
    {
        public string? Endpoint { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Endpoint = request.RequestUri!.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) });
        }
    }
}
