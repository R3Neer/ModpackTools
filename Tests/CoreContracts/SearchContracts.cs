using System.Net;
using System.Text;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Providers;

internal static class SearchContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools search " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string body = """{"total_hits":9007199254740993,"hits":[{"project_id":"P1","slug":"shader","project_type":"sha\u00ADder","title":"A","author":null,"description":"","downloads":9007199254740993}]}""";
            var handler = new Reply(body);
            var view = await ProviderSearch.ReadAsync("café + x", "shaders", 2, null, directory, DateTimeOffset.UtcNow, handler: handler);
            if (view.Results.Items is not [{ Type: "shaderpack", Author: "", Downloads: 9007199254740993 }] || view.Type != "shaderpack" || view.Results.TotalHits != 9007199254740993)
                throw new Exception("Search projection lost type/null/Int64 facts.");
            if (!handler.Endpoint!.Contains("query=caf%C3%A9%20%2B%20x", StringComparison.Ordinal) || Uri.UnescapeDataString(handler.Endpoint).Contains("categories:", StringComparison.Ordinal))
                throw new Exception("Global search endpoint is incorrect.");
            var path = Path.Combine(directory, "last-search.json"); var original = File.ReadAllBytes(path);
            using (var cache = JsonDocument.Parse(original))
                if (cache.RootElement.GetProperty("ProjectId").ValueKind != JsonValueKind.Null || cache.RootElement.GetProperty("Results")[0].GetProperty("Downloads").GetInt64() != 9007199254740993)
                    throw new Exception("Search cache context or count changed.");
            try { await ProviderSearch.ReadAsync("x", "all", 1, null, directory, DateTimeOffset.UtcNow, cache: new ReferenceCacheWriteLimits(1), handler: new Reply(body)); throw new Exception("Cache quota did not fail."); }
            catch (DomainException exception) when (exception.Id == "ModpackTools.Search.CacheWriteLimit") { }
            if (!original.SequenceEqual(File.ReadAllBytes(path))) throw new Exception("Failed search replaced previous references.");
            if (Directory.GetFiles(Path.Combine(directory, "metadata")).Length != 0) throw new Exception("Search seeded provider metadata.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Reply(string body) : HttpMessageHandler
    {
        public string? Endpoint { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Endpoint = request.RequestUri!.OriginalString; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }); }
    }
}
