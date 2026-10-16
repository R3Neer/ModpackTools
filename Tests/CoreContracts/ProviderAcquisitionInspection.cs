using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Providers;

internal static class ProviderAcquisitionInspection
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length is not (3 or 5) || args.Length == 5 && args[3] != "--base-uri")
                throw new ArgumentException("Use --inspect-provider-acquisition <config> <case-json> [--base-uri loopback-uri].");
            using var input = new FileStream(args[2], FileMode.Open, FileAccess.Read, FileShare.Read);
            using var bytes = new MemoryStream(); var buffer = new byte[4096]; int read;
            while ((read = input.Read(buffer)) != 0)
            { if (read > 4 * 1024 * 1024 - bytes.Length) throw new ArgumentException("Acquisition case exceeds4MiB."); bytes.Write(buffer, 0, read); }
            using var document = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), new() { MaxDepth = 64 });
            var root = document.RootElement;
            var now = DateTimeOffset.Parse(root.GetProperty("Now").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var limits = new ProviderRequestLimits();
            if (root.TryGetProperty("Limits", out var policy))
            {
                foreach (var option in policy.EnumerateObject()) limits = option.Name switch
                {
                    "Decode" => limits with { Decode = DecodePolicy(option.Value) },
                    "MaximumRequests" => limits with { MaximumRequests = option.Value.GetInt32() },
                    "MaximumConcurrency" => limits with { MaximumConcurrency = option.Value.GetInt32() },
                    "TimeoutMilliseconds" => limits with { TimeoutMilliseconds = option.Value.GetInt32() },
                    "MaximumResponseBytes" => limits with { MaximumResponseBytes = option.Value.GetInt64() },
                    "MaximumTotalResponseBytes" => limits with { MaximumTotalResponseBytes = option.Value.GetInt64() },
                    "MaximumInputIds" => limits with { MaximumInputIds = option.Value.GetInt32() },
                    "MaximumEndpointCharacters" => limits with { MaximumEndpointCharacters = option.Value.GetInt32() },
                    "MaximumTotalEndpointCharacters" => limits with { MaximumTotalEndpointCharacters = option.Value.GetInt32() },
                    "MaximumReturnedVersions" => limits with { MaximumReturnedVersions = option.Value.GetInt32() },
                    _ => throw new ArgumentException("Unknown acquisition quota.")
                };
            }
            var calls = new List<string>(); var active = 0; var peak = 0;
            var replies = root.TryGetProperty("Responses", out var responses) ? responses : default;
            HttpMessageHandler? handler = args.Length == 5 ? null : new ProviderAcquisitionContracts.Handler(async (request, token) =>
            {
                var endpoint = request.RequestUri!.PathAndQuery[4..];
                lock (calls) { calls.Add(endpoint); active++; peak = Math.Max(peak, active); }
                try
                {
                    var response = replies.GetProperty(endpoint);
                    if (response.TryGetProperty("DelayMilliseconds", out var delay)) await Task.Delay(delay.GetInt32(), token);
                    var result = ProviderAcquisitionContracts.Response(response.GetProperty("Body").GetString()!);
                    if (response.TryGetProperty("Status", out var status)) result.StatusCode = (HttpStatusCode)status.GetInt32();
                    return result;
                }
                finally { lock (calls) active--; }
            });
            using var cancellation = new CancellationTokenSource();
            if (root.TryGetProperty("CancelAfterMilliseconds", out var cancel)) cancellation.CancelAfter(cancel.GetInt32());
            await using var client = new ModrinthVersionClient(args[1], now, limits, cancellation.Token, handler,
                args.Length == 5 ? new Uri(args[4]) : null);
            var results = new List<Outcome>();
            var operations = root.GetProperty("Operations").EnumerateArray().ToArray();
            if (operations.Length > 10000) throw new ArgumentException("Too many acquisition operations.");
            if (root.TryGetProperty("Parallel", out var parallel) && parallel.GetBoolean())
                results.AddRange(await Task.WhenAll(operations.Select(Invoke)));
            else foreach (var operation in operations) results.Add(await Invoke(operation));
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteNumber("Requests", client.Statistics.Requests); writer.WriteNumber("Bytes", client.Statistics.Bytes);
                writer.WriteNumber("PeakHeaders", peak); writer.WriteStartArray("Endpoints"); foreach (var call in calls) writer.WriteStringValue(call); writer.WriteEndArray();
                writer.WriteStartArray("Results"); foreach (var result in results)
                {
                    writer.WriteStartObject(); writer.WriteBoolean("Allowed", result.Error is null);
                    if (result.Error is { } error)
                    { writer.WriteString("Id", error.Id); writer.WriteString("Message", error.Message); writer.WriteString("Category", error.Category); writer.WriteString("Hint", error.Hint); writer.WriteString("Details", error.Details); writer.WriteString("Target", (error.Target as TextTarget)?.Value); }
                    else { writer.WriteStartArray("Versions"); foreach (var version in result.Versions) ProviderCacheInspection.WriteVersion(writer, version); writer.WriteEndArray(); }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteStartArray("Diagnostics"); foreach (var diagnostic in client.Diagnostics)
                { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Code); writer.WriteString("Path", diagnostic.Path); writer.WriteString("Message", diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, (int)output.Length); return 0;
            async Task<Outcome> Invoke(JsonElement operation)
            {
                try
                {
                    if (operation.TryGetProperty("Id", out var id)) return new([await client.GetVersionAsync(id.GetString()!)], null);
                    var ids = operation.GetProperty("Ids").EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Null ? null : value.GetString());
                    return new(await client.GetVersionsAsync(ids), null);
                }
                catch (DomainException exception) { return new([], exception); }
                catch (OperationCanceledException) { return new([], new("Core.OperationCancelled", "Provider lookup was cancelled", "retry when ready", "OperationStopped")); }
            }
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check acquisition inspection input", null); return 2; }
    }
    private static ProviderCacheReadLimits DecodePolicy(JsonElement value)
    {
        var limits = new ProviderCacheReadLimits();
        foreach (var option in value.EnumerateObject()) limits = option.Name switch
        {
            "MaximumBytes" => limits with { MaximumBytes = option.Value.GetInt64() },
            "MaximumDepth" => limits with { MaximumDepth = option.Value.GetInt32() },
            "MaximumNormalizedCharacters" => limits with { MaximumNormalizedCharacters = option.Value.GetInt32() },
            "MaximumTokenCharacters" => limits with { MaximumTokenCharacters = option.Value.GetInt32() },
            "MaximumNodes" => limits with { MaximumNodes = option.Value.GetInt32() },
            "MaximumCollectionEntries" => limits with { MaximumCollectionEntries = option.Value.GetInt32() },
            _ => throw new ArgumentException("Unknown provider decode quota.")
        };
        return limits;
    }
    private sealed record Outcome(ImmutableArray<ProviderVersion> Versions, DomainException? Error);
}
