using System.Text.Json;
using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Resolution;

internal static class ContentMaterializationInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-content-materialization <request.json>.");
            using var file = File.OpenRead(args[1]);
            if (file.Length > 4 * 1024 * 1024) throw new ArgumentException("Materialization request exceeds4MiB.");
            using var json = JsonDocument.Parse(file, new() { MaxDepth = 32 }); var input = json.RootElement;
            var observed = Context(Text(input, "ObservedRoot")); var stage = Context(Text(input, "StageRoot"));
            var operation = Text(input, "Operation") switch { "add" => ContentPlanOperation.Add, "update" => ContentPlanOperation.Update, "repair" => ContentPlanOperation.Repair, _ => throw new ArgumentException("Invalid operation.") };
            var rows = input.GetProperty("Nodes"); if (rows.GetArrayLength() > 1024) throw new ArgumentException("Too many fixture nodes.");
            var nodes = rows.EnumerateArray().Select(Node).ToImmutableArray();
            var changes = input.GetProperty("Changes").EnumerateArray().Select(row =>
            {
                var id = Text(row, "Id"); var node = nodes.Single(node => node.Graph.Id == id);
                var path = Text(row, "BeforeMetadataPath");
                return new PlannedContentChange(id, path.Length == 0 ? null : node with { Item = node.Item with { MetadataPath = path } }, node, "fixture");
            }).ToImmutableArray();
            var requested = input.GetProperty("Requested").EnumerateArray().Select(value => value.GetString()!).ToImmutableArray();
            var report = GraphReportBuilder.Build(new("1.21", "fabric", "0.16"), []);
            var plan = new ContentPlan(nodes, changes, requested, report, report, operation);
            var limits = new ContentMaterializationLimits();
            if (input.TryGetProperty("MaximumDocumentCharacters", out var maximum)) limits = limits with { MaximumDocumentCharacters = maximum.GetInt32() };
            if (input.TryGetProperty("MaximumOutputBytes", out var bytes)) limits = limits with { MaximumOutputBytes = bytes.GetInt64() };
            using var cancellation = new CancellationTokenSource(); if (input.TryGetProperty("Cancelled", out var cancelled) && cancelled.GetBoolean()) cancellation.Cancel();
            var result = ContentMaterialization.Apply(observed, stage, plan, limits, cancellation.Token);
            using var capture = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(capture))
            {
                writer.WriteStartObject(); writer.WriteBoolean("EditorialWritten", result.EditorialWritten); writer.WriteStartArray("Files");
                foreach (var item in result.Files)
                { writer.WriteStartObject(); writer.WriteString("Id", item.Id); writer.WriteString("MetadataPath", item.MetadataPath); writer.WriteString("VersionId", item.VersionId); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(capture.GetBuffer(), 0, checked((int)capture.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check materialization fixture", null); return 2; }
    }

    internal static ProjectContext Context(string root) => new("fixture", Path.GetFullPath(root), "Fixture", "1", "fixture.mrpack", "1.21", "fabric", "0.16", "index.toml", Path.Combine(Path.GetFullPath(root), "index.toml"));
    private static ProjectContentNode Node(JsonElement row)
    {
        var kind = Text(row, "Kind") switch { "mod" => ContentKind.Mod, "resourcepack" => ContentKind.ResourcePack, "shaderpack" => ContentKind.ShaderPack, _ => throw new ArgumentException("Invalid kind.") };
        var id = Text(row, "Id"); var version = Text(row, "VersionId"); var name = Text(row, "Name"); var side = Text(row, "Side", "both");
        var item = new InventoryItem(id, kind, name, Text(row, "Filename"), side, ContentSource.Packwiz, version, Text(row, "MetadataPath"), false);
        var graph = new GraphNode(id, version, name, Text(row, "Kind"), side, true, [], [], []);
        var hasFile = !row.TryGetProperty("HasFile", out var flag) || flag.GetBoolean();
        var selected = hasFile ? new ProviderFile(Text(row, "Filename"), Text(row, "Url"), true, Text(row, "Sha1"), Text(row, "Sha512"), null) : null;
        return new(item, graph, null, selected, false, Text(row, "Intent", "transitive"));
    }
    private static string Text(JsonElement input, string field, string fallback = "") => input.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString()! : fallback;
}
