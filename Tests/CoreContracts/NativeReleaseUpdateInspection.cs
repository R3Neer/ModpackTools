using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core;
using ModpackTools.Core.Distribution;

internal static class NativeReleaseUpdateInspection
{
    public static int Run(string[] args)
    {
        DownloadHandler? handler = null;
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-native-release-update <request.json>");
            using var input = File.OpenRead(args[1]); if (input.Length > 65536) throw new ArgumentException("Release-update request exceeds its quota");
            using var document = JsonDocument.Parse(input); var request = document.RootElement;
            var metadata = File.ReadAllBytes(request.GetProperty("MetadataPath").GetString()!);
            var release = NativeReleaseChannel.Parse(metadata);
            handler = new(release.Asset!.Url, request.GetProperty("ArchivePath").GetString()!);
            var result = NativeReleaseUpdate.ApplyAsync(request.GetProperty("Root").GetString()!, request.GetProperty("StateDirectory").GetString()!,
                Environment.ProcessPath!, release, request.GetProperty("ExpectedGeneration").GetString()!, handler: handler).GetAwaiter().GetResult();
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput()); writer.WriteStartObject(); writer.WriteBoolean("ok", true);
            writer.WriteNumber("download_calls", handler.Calls); writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, result, ReleaseUpdateInspectionJson.Default.NativeInstallationResult); writer.WriteEndObject(); return 0;
        }
        catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput()); writer.WriteStartObject(); writer.WriteBoolean("ok", false);
            writer.WriteNumber("download_calls", handler?.Calls ?? 0); writer.WriteString("id", (error as DomainException)?.Id);
            writer.WriteString("target", (error as DomainException)?.Target is TextTarget target ? target.Value : null);
            writer.WriteString("message", error.Message); writer.WriteBoolean("retain_stage", error.Data["ModpackTools.RetainStage"] is true);
            writer.WriteBoolean("version_published", error.Data["ModpackTools.Installation.VersionPublished"] is true);
            writer.WriteEndObject(); return 1;
        }
        finally { handler?.Dispose(); }
    }

    private sealed class DownloadHandler(string expectedUri, string archive) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Calls++;
            if (message.RequestUri?.AbsoluteUri != expectedUri) throw new InvalidOperationException("Unexpected release download URI");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read)) });
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(NativeInstallationResult))]
internal sealed partial class ReleaseUpdateInspectionJson : JsonSerializerContext;
