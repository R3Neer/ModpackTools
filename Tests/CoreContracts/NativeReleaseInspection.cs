using System.Net;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Distribution;

internal static class NativeReleaseInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-native-release <request.json>");
            using var input = File.OpenRead(args[1]); if (input.Length > 65536) throw new ArgumentException("Release request exceeds its quota");
            using var document = JsonDocument.Parse(input); var request = document.RootElement;
            var online = request.TryGetProperty("Online", out var flag) && flag.GetBoolean();
            using var cancellation = new CancellationTokenSource();
            if (request.TryGetProperty("CancelBefore", out var cancel) && cancel.GetBoolean()) cancellation.Cancel();
            using var fixture = online ? null : new FixtureHandler(request);
            var release = NativeReleaseChannel.LatestAsync(cancellation.Token, fixture).GetAwaiter().GetResult();
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput()); writer.WriteStartObject(); writer.WriteBoolean("ok", true);
            writer.WriteString("version", release.Version); writer.WriteString("tag", release.Tag); writer.WriteString("url", release.Url);
            writer.WriteBoolean("native_available", release.Asset is not null); writer.WriteString("unavailable_reason", release.UnavailableReason);
            writer.WriteString("asset_url", release.Asset?.Url); writer.WriteString("sha256", release.Asset?.Sha256);
            if (release.Asset is null) writer.WriteNull("length"); else writer.WriteNumber("length", release.Asset.Length);
            writer.WriteEndObject(); return 0;
        }
        catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput()); writer.WriteStartObject(); writer.WriteBoolean("ok", false);
            writer.WriteString("id", (error as DomainException)?.Id); writer.WriteString("message", error.Message); writer.WriteEndObject(); return 1;
        }
    }

    private sealed class FixtureHandler(JsonElement request) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (message.RequestUri?.AbsoluteUri != NativeReleaseChannel.LatestUri || !message.Headers.Contains("X-GitHub-Api-Version"))
                throw new InvalidOperationException("Release client did not use the official pinned endpoint");
            var status = request.TryGetProperty("Status", out var value) ? (HttpStatusCode)value.GetInt32() : HttpStatusCode.OK;
            var bytes = request.TryGetProperty("Oversize", out var oversize) && oversize.GetBoolean()
                ? new byte[512 * 1024 + 1] : File.ReadAllBytes(request.GetProperty("MetadataPath").GetString()!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) });
        }
    }
}
