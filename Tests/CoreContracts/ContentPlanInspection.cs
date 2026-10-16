using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

internal static class ContentPlanInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 6) throw new ArgumentException("Use --inspect-content-plan <project-root> <config> <request-json> <now> <base-uri>.");
            using var input = File.OpenRead(args[3]); if (input.Length > 4 * 1024 * 1024) throw new ArgumentException("Plan request exceeds4MiB.");
            using var document = JsonDocument.Parse(input, new() { MaxDepth = 64 }); var root = document.RootElement;
            var operation = Text("Operation", "add") switch { "add" => ContentPlanOperation.Add, "update" => ContentPlanOperation.Update, "repair" => ContentPlanOperation.Repair, _ => throw new ArgumentException("Unsupported plan operation.") };
            var selectors = root.TryGetProperty("Selectors", out var values) ? values.EnumerateArray().Select(value => value.GetString()!).ToImmutableArray() : [];
            var request = new ContentPlanRequest(operation, selectors, Flag("All"), Text("Type", "all"), Text("To", ""), Flag("Strict"), Flag("AllowDowngrade"));
            var plan = ContentPlan.PrepareAsync(ProjectReader.Read(args[1]), request, args[2], DateTimeOffset.Parse(args[4], CultureInfo.InvariantCulture), providerBaseUri: new(args[5])).GetAwaiter().GetResult();
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output, new() { MaxDepth = 256 }))
            {
                writer.WriteStartObject(); writer.WriteStartObject("Nodes");
                foreach (var node in plan.Nodes) { writer.WritePropertyName(node.Graph.Id); CandidateAcquisitionInspection.WriteNode(writer, node); }
                writer.WriteEndObject(); writer.WriteStartArray("Changes");
                foreach (var change in plan.Changes)
                {
                    writer.WriteStartObject(); writer.WriteString("Id", change.Id); writer.WriteString("Reason", change.Reason);
                    writer.WritePropertyName("Before"); if (change.Before is { } before) CandidateAcquisitionInspection.WriteNode(writer, before); else writer.WriteNullValue();
                    writer.WritePropertyName("After"); CandidateAcquisitionInspection.WriteNode(writer, change.After); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteStartArray("Requested"); foreach (var id in plan.Requested) writer.WriteStringValue(id); writer.WriteEndArray();
                writer.WritePropertyName("Baseline"); GraphInspection.WriteReport(writer, plan.Baseline); writer.WritePropertyName("Report"); GraphInspection.WriteReport(writer, plan.Report);
                writer.WriteString("Operation", plan.Operation.ToString().ToLowerInvariant()); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
            string Text(string field, string fallback) => root.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString()! : fallback;
            bool Flag(string field) => root.TryGetProperty(field, out var value) && value.GetBoolean();
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check plan inspection arguments", null); return 2; }
    }
}
