using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Providers;

internal static class ArtifactProviderInspection
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("Use --inspect-artifact <config> <case-json>.");
            using var input = new FileStream(args[2], FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 1024 * 1024) throw new ArgumentException("Artifact case exceeds1MiB.");
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            var policy = new ArtifactProviderLimits();
            if (root.TryGetProperty("Limits", out var limits)) foreach (var property in limits.EnumerateObject())
                policy = property.Name switch
                {
                    "MaximumArtifactBytes" => policy with { MaximumArtifactBytes = property.Value.GetInt64() },
                    "MaximumTotalBytes" => policy with { MaximumTotalBytes = property.Value.GetInt64() },
                    "MaximumRequests" => policy with { MaximumRequests = property.Value.GetInt32() },
                    "MaximumOperations" => policy with { MaximumOperations = property.Value.GetInt32() },
                    "MaximumConcurrency" => policy with { MaximumConcurrency = property.Value.GetInt32() },
                    "TimeoutMilliseconds" => policy with { TimeoutMilliseconds = property.Value.GetInt32() },
                    _ => throw new ArgumentException("Unknown artifact policy.")
                };
            using var cancellation = new CancellationTokenSource();
            if (root.TryGetProperty("CancelAfterMilliseconds", out var cancel)) cancellation.CancelAfter(cancel.GetInt32());
            await using var provider = new ArtifactProvider(args[1], policy, cancellation.Token);
            var request = new ArtifactRequest(Text("Url"), Text("Hash"), Text("Algorithm"),
                root.TryGetProperty("LocalPath", out var local) ? local.GetString() : null,
                root.TryGetProperty("Check", out var check) && check.GetBoolean());
            var result = await provider.ReadAsync(request, root.TryGetProperty("Loader", out var loader) ? loader.GetString()! : "fabric");
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteBoolean("Available", result.Available); writer.WriteString("Source", result.Source?.ToString());
                writer.WriteString("Path", result.Path); writer.WriteString("Algorithm", result.Algorithm); writer.WriteString("Hash", result.Hash);
                writer.WriteString("TraversalStatus", result.Declarations?.Status.ToString());
                writer.WriteStartArray("Mods");
                if (result.Declarations is { } declarations) foreach (var mod in declarations.Mods)
                {
                    writer.WriteStartObject(); writer.WriteString("Id", mod.Declaration.Id); writer.WriteString("Version", mod.Declaration.Version);
                    writer.WriteString("Side", mod.Declaration.Side); writer.WriteBoolean("Nested", mod.Nested);
                    writer.WriteStartArray("EntryChain"); foreach (var entry in mod.EntryChain) writer.WriteStringValue(entry);
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteStartArray("Diagnostics");
                if (result.Declarations is { } observed) foreach (var diagnostic in observed.Diagnostics)
                { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Diagnostic.Code); writer.WriteString("Message", diagnostic.Diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteStartObject("Statistics"); var statistics = provider.Statistics;
                writer.WriteNumber("Requests", statistics.Requests); writer.WriteNumber("NetworkBytes", statistics.NetworkBytes);
                writer.WriteNumber("VerificationBytes", statistics.VerificationBytes); writer.WriteEndObject(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, (int)output.Length); return 0;
            string Text(string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check artifact inspection arguments", null); return 2; }
    }
}
