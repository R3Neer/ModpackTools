using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Transactions;

internal static class ContentOperationInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-content-operation <request.json>.");
            using var input = File.OpenRead(args[1]); if (input.Length > 4 * 1024 * 1024) throw new ArgumentException("Content request exceeds4MiB.");
            using var document = JsonDocument.Parse(input, new() { MaxDepth = 32 }); var root = document.RootElement;
            var project = ProjectReader.Read(Text(root, "Root"));
            var operation = Text(root, "Operation") switch { "add" => ContentPlanOperation.Add, "update" => ContentPlanOperation.Update, "repair" => ContentPlanOperation.Repair, _ => throw new ArgumentException("Invalid operation.") };
            var selectors = root.TryGetProperty("Selectors", out var selected) ? selected.EnumerateArray().Select(value => value.GetString()!).ToImmutableArray() : [];
            var request = new ContentOperationRequest(new(operation, selectors, Flag(root, "All"), Text(root, "Type", "all"), Optional(root, "To"),
                Flag(root, "Strict"), Flag(root, "AllowDowngrade")), Flag(root, "DryRun"), Optional(root, "Category"), root.TryGetProperty("EnableAt", out var enable) ? enable.GetInt32() : null);
            var now = root.TryGetProperty("Now", out var date) ? DateTimeOffset.Parse(date.GetString()!, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow;
            var endpoint = Optional(root, "ProviderBaseUri");
            using var cancellation = new CancellationTokenSource();
            var result = ContentOperation.ExecuteAsync(project, request, Text(root, "ConfigurationDirectory"), Text(root, "StateDirectory"), now,
                () => new PackwizObservation(true, Text(root, "PackwizExecutable"), "explicit", null), Optional(root, "NativeHost") ?? Environment.ProcessPath!,
                cancellation: cancellation.Token, providerBaseUri: endpoint is null ? null : new Uri(endpoint), prepareOutput: PrepareOutput,
                observer: value => { if (value.Phase == TransactionPhase.Committed && Flag(root, "CancelAtCommitted")) cancellation.Cancel(); return ValueTask.CompletedTask; }).GetAwaiter().GetResult();
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteBoolean("Applied", result.Applied); writer.WriteString("BeforeFingerprint", result.Expectation.BeforeFingerprint);
                writer.WriteStartArray("Changes"); foreach (var change in result.Changes)
                { writer.WriteStartObject(); writer.WriteString("Path", change.Path); writer.WriteString("Before", change.Before); writer.WriteString("After", change.After); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteStartObject("Plan"); writer.WriteString("Operation", result.Result.Operation.ToString().ToLowerInvariant());
                writer.WriteStartArray("Requested"); foreach (var id in result.Result.Requested) writer.WriteStringValue(id); writer.WriteEndArray();
                writer.WriteStartArray("Changes"); foreach (var change in result.Result.Changes)
                {
                    writer.WriteStartObject(); writer.WriteString("Id", change.Id); writer.WriteString("Reason", change.Reason); writer.WriteString("BeforeVersionId", change.Before?.Graph.VersionId);
                    writer.WriteString("AfterVersionId", change.After.Graph.VersionId); writer.WriteString("Name", change.After.Item.Name); writer.WriteString("MetadataPath", change.After.Item.MetadataPath); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteStartArray("Nodes"); foreach (var node in result.Result.Nodes)
                { writer.WriteStartObject(); writer.WriteString("Id", node.Graph.Id); writer.WriteString("VersionId", node.Graph.VersionId); writer.WriteString("Intent", node.Intent); writer.WriteString("MetadataPath", node.Item.MetadataPath); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteNumber("Errors", result.Result.Report.Errors.Length); writer.WriteNumber("Unknown", result.Result.Report.Unknown.Length);
                writer.WriteNumber("Warnings", result.Result.Report.Warnings.Length); writer.WriteEndObject(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;

            ValueTask PrepareOutput(TransactionPreview<ContentPlan> preview, CancellationToken token)
            {
                if (Flag(root, "FailOutput")) throw new DomainException("Core.OutputLimitExceeded", "Fixture public output exceeds its quota", "use a bounded result", "LimitsExceeded");
                return ValueTask.CompletedTask;
            }
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check content operation fixture", null); return 2; }
    }
    private static string Text(JsonElement value, string name, string fallback = "") => Optional(value, name) ?? fallback;
    private static string? Optional(JsonElement value, string name) => value.TryGetProperty(name, out var result) && result.ValueKind != JsonValueKind.Null ? result.GetString() : null;
    private static bool Flag(JsonElement value, string name) => value.TryGetProperty(name, out var result) && result.GetBoolean();
}
