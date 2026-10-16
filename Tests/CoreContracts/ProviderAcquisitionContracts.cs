using System.Net;
using System.Net.Sockets;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Source;

internal static class ProviderAcquisitionContracts
{
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls;
        public bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return respond(request, cancellationToken); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    internal static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static async Task VerifyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "modpack-acquire-" + Guid.NewGuid().ToString("N"));
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(root);
        try
        {
            var handler = new Handler(async (request, token) =>
            {
                if (request.Method != HttpMethod.Get || request.Headers.UserAgent.ToString() != "R3Neer-ModpackTools/" + ProductVersion.Current.Version) throw new InvalidOperationException("Provider request contract changed.");
                await Task.Delay(25, token);
                return Response("{\"id\":\"v1\",\"date_published\":\"2024-01-02T03:04:05Z\",\"files\":[{\"primary\":true,\"size\":9223372036854775807}]}");
            });
            await using (var client = new ModrinthVersionClient(root, now, handler: handler))
            {
                var values = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetVersionAsync("v1")));
                if (handler.Calls != 1 || client.Statistics.Requests != 1 || values.Any(value => !ReferenceEquals(value, values[0]))) throw new InvalidOperationException("Provider endpoint was not shared.");
                var cached = ProviderVersionCache.Read(root, "v1", now);
                if (cached.Version?.Published.Text != values[0].Published.Text || cached.ModifiedUtc != now.UtcDateTime
                    || cached.Version.Files[0].Size != long.MaxValue) throw new InvalidOperationException("Checked metadata did not roundtrip.");
            }
            if (!handler.Disposed) throw new InvalidOperationException("HTTP handler ownership leaked.");
            var path = Path.Combine(root, "metadata", TextHash.Compute("version/v1") + ".json");
            var original = File.ReadAllBytes(path);
            await Failure("Metadata.InvalidProviderResponse", "{", null);
            await Failure("Metadata.InvalidProviderResponse", "{\"id\":\"v2\"}", null);
            await Failure("Metadata.ProviderRequestLimit", "{\"id\":\"v1\"}", new(MaximumResponseBytes: 1));
            if (!File.ReadAllBytes(path).SequenceEqual(original)) throw new InvalidOperationException("Bad response overwrote a valid cache.");
            await using (var client = new ModrinthVersionClient(root, now, handler: new Handler((_, _) => Task.FromResult(Response("[{\"id\":\"v1\",\"name\":\"Early\"},{\"id\":\"v1\",\"name\":\"Late\"}]")))))
            {
                var values = await client.GetVersionsAsync(["v1", "v1", null, "", "v2"]);
                if (values.Length != 2 || values[0].Name != "Early" || values[1].Name != "Late" || ProviderVersionCache.Read(root, "v1", now).Version?.Name != "Late") throw new InvalidOperationException("Batch response order/last cache occurrence changed.");
                if (!(await client.GetVersionsAsync([])).IsEmpty || client.Statistics.Requests != 1) throw new InvalidOperationException("Empty batch performed HTTP.");
            }
            original = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                await Failure("Metadata.ProviderCacheWriteFailed", "{\"id\":\"v1\"}", null);
            if (!File.ReadAllBytes(path).SequenceEqual(original)) throw new InvalidOperationException("Failed replacement changed locked cache.");
            var slow = new Handler(async (_, token) => { await Task.Delay(1000, token); return Response("{\"id\":\"v1\"}"); });
            await using (var client = new ModrinthVersionClient(root, now, new(TimeoutMilliseconds: 10), handler: slow))
            {
                try { await client.GetVersionAsync("v1"); throw new InvalidOperationException("Deadline ignored."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Versions.RequestTimeout") { }
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await using (var client = new ModrinthVersionClient(root, now, cancellation: cancel.Token, handler: new Handler((_, _) => throw new InvalidOperationException("Cancelled HTTP ran."))))
            {
                try { await client.GetVersionsAsync([]); throw new InvalidOperationException("Empty-batch cancellation ignored."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Core.OperationCancelled") { }
                try { await client.GetVersionAsync("v1"); throw new InvalidOperationException("Cancellation ignored."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Core.OperationCancelled") { }
            }
            await Loopback(root, now);
            if (Directory.EnumerateFiles(Path.Combine(root, "metadata"), "*.tmp").Any()) throw new InvalidOperationException("Owned HTTP temporary leaked.");
            async Task Failure(string id, string body, ProviderRequestLimits? limits)
            {
                await using var client = new ModrinthVersionClient(root, now, limits, handler: new Handler((_, _) => Task.FromResult(Response(body))));
                try { await client.GetVersionAsync("v1"); throw new InvalidOperationException("Expected acquisition failure " + id); }
                catch (DomainException error) when (error.Id == "ModpackTools." + id) { }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task Loopback(string root, DateTimeOffset now)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line != "GET /v2/version/v3 HTTP/1.1") throw new InvalidOperationException("Native HTTP did not use the typed provider endpoint.");
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }
            var body = Encoding.UTF8.GetBytes("{\"id\":\"v3\",\"name\":\"Loopback\"}");
            var headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, timeout.Token); await stream.WriteAsync(body, timeout.Token);
        }, timeout.Token);
        try
        {
            await using var client = new ModrinthVersionClient(root, now, cancellation: timeout.Token, baseUri: new("http://127.0.0.1:" + port + "/v2/"));
            if ((await client.GetVersionAsync("v3")).Name != "Loopback") throw new InvalidOperationException("Native loopback acquisition failed.");
            await serving;
        }
        finally { listener.Stop(); timeout.Cancel(); }
    }
}
