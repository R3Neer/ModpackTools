using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Resolution;

internal static class RemovalPlanningInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-removal-plan <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 4 * 1024 * 1024) throw new ArgumentException("Input exceeds 4 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 256 }); var input = document.RootElement;
            if (input.TryGetProperty("Culture", out var culture)) CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture.GetString()!);
            var model = GraphInspection.ReadModel(input); var entries = input.GetProperty("Nodes").EnumerateArray().ToArray();
            var nodes = model.Nodes.Select((graph, index) =>
            {
                var node = entries[index]; var item = node.TryGetProperty("Item", out var physical) ? physical : node;
                var kind = graph.Kind switch { "mod" => ContentKind.Mod, "resourcepack" => ContentKind.ResourcePack, "shaderpack" => ContentKind.ShaderPack, _ => throw new ArgumentException("Unknown physical kind.") };
                var source = Text(item, "Source", "packwiz") switch { "local" => ContentSource.Local, "packwiz" => ContentSource.Packwiz, _ => throw new ArgumentException("Unknown physical source.") };
                var pinned = Flag(node, "Pinned");
                return new ProjectContentNode(new(graph.Id, kind, graph.Name, Text(item, "Filename", graph.Id + ".jar"), graph.Side, source, graph.VersionId,
                    item.TryGetProperty("MetadataPath", out var path) ? path.GetString() : source == ContentSource.Local ? null : graph.Id + ".pw.toml", pinned), graph, null, null, pinned, Text(node, "Intent", "explicit"));
            }).ToImmutableArray();
            var request = new RemovalPlanRequest([.. input.GetProperty("Selectors").EnumerateArray().Select(value => value.GetString()!)], Text(input, "Type", "all"), Flag(input, "Cascade"), Flag(input, "AutoRemove"), Flag(input, "Strict"));
            var limits = input.TryGetProperty("Limits", out var policy)
                ? new RemovalPlanningLimits(MaximumGraphPasses: Number(policy, "MaximumGraphPasses", 1024), MaximumWork: Long(policy, "MaximumWork", 100000000),
                    MaximumEdges: Number(policy, "MaximumEdges", 1000000), MaximumDetailCharacters: Number(policy, "MaximumDetailCharacters", 1024 * 1024)) : new();
            using var cancellation = new CancellationTokenSource(); if (Flag(input, "Cancelled")) cancellation.Cancel();
            var plan = RemovalPlanning.Prepare(model.Environment, nodes, request, model.Overrides, limits, cancellation.Token);
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output, new() { MaxDepth = 256 }))
            {
                writer.WriteStartObject(); writer.WriteStartArray("Requested"); foreach (var id in plan.Requested) writer.WriteStringValue(id); writer.WriteEndArray();
                writer.WriteStartObject("Nodes"); foreach (var node in plan.Nodes) { writer.WritePropertyName(node.Graph.Id); CandidateAcquisitionInspection.WriteNode(writer, node); }
                writer.WriteEndObject();
                writer.WriteStartArray("Changes"); foreach (var change in plan.Changes)
                {
                    writer.WriteStartObject(); writer.WriteString("Id", change.Id); writer.WriteString("Reason", change.Reason); writer.WritePropertyName("Before"); CandidateAcquisitionInspection.WriteNode(writer, change.Before);
                    writer.WriteNull("After"); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WritePropertyName("Baseline"); GraphInspection.WriteReport(writer, plan.Baseline); writer.WritePropertyName("Report"); GraphInspection.WriteReport(writer, plan.Report); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check pure removal input", null); return 2; }
    }
    private static string Text(JsonElement value, string field, string fallback) => value.TryGetProperty(field, out var item) ? item.GetString()! : fallback;
    private static bool Flag(JsonElement value, string field) => value.TryGetProperty(field, out var item) && item.GetBoolean();
    private static int Number(JsonElement value, string field, int fallback) => value.TryGetProperty(field, out var item) ? item.GetInt32() : fallback;
    private static long Long(JsonElement value, string field, long fallback) => value.TryGetProperty(field, out var item) ? item.GetInt64() : fallback;
}
