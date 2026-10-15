using System.Net;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Dependencies;

internal static class DependencyDownloadInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-dependency-download <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 }); var input = document.RootElement;
            using var handler = new FixtureHandler(input);
            using var cancellation = new CancellationTokenSource(); if (input.TryGetProperty("Cancelled", out var cancelled) && cancelled.GetBoolean()) cancellation.Cancel();
            var limits = new DependencyDownloadLimits(
                input.TryGetProperty("MaximumBytes", out var bytes) ? bytes.GetInt64() : 256 * 1024 * 1024,
                input.TryGetProperty("TimeoutMilliseconds", out var timeout) ? timeout.GetInt32() : 60000);
            var result = DependencyDownload.FetchAsync(new Uri(input.GetProperty("Uri").GetString()!), input.GetProperty("ExpectedSha256").GetString()!,
                input.GetProperty("StagingParent").GetString()!, limits, cancellation.Token, handler).GetAwaiter().GetResult();
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput()); writer.WriteStartObject();
            writer.WriteString("Path", result.Path); writer.WriteString("Sha256", result.Sha256); writer.WriteNumber("Length", result.Length); writer.WriteNumber("Requests", handler.Requests); writer.WriteEndObject(); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check dependency download fixture", null); return 2; }
    }
    private sealed class FixtureHandler(JsonElement input) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Requests++;
            if (input.TryGetProperty("RequestLogPath", out var log)) File.AppendAllText(log.GetString()!, request.RequestUri + Environment.NewLine);
            if (input.TryGetProperty("Failure", out var failure)) throw new HttpRequestException(failure.GetString());
            var status = input.TryGetProperty("Status", out var value) ? (HttpStatusCode)value.GetInt32() : HttpStatusCode.OK;
            var response = new HttpResponseMessage(status) { RequestMessage = request };
            if (input.TryGetProperty("Redirect", out var redirect)) response.Headers.Location = new Uri(redirect.GetString()!, UriKind.RelativeOrAbsolute);
            if (input.TryGetProperty("ResponsePath", out var path))
            {
                Stream body = new FileStream(path.GetString()!, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (input.TryGetProperty("OmitContentLength", out var omit) && omit.GetBoolean()) body = new NonSeekable(body);
                response.Content = new StreamContent(body);
                if (input.TryGetProperty("ContentLength", out var declared)) response.Content.Headers.ContentLength = declared.GetInt64();
            }
            else response.Content = new ByteArrayContent([]);
            return Task.FromResult(response);
        }
    }
    private sealed class NonSeekable(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default) => inner.ReadAsync(buffer, cancellation);
        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
