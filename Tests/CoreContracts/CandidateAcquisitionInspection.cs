using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Resolution;

internal static class CandidateAcquisitionInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 7 || (arguments.Length - 7) % 2 != 0)
                throw new ArgumentException("Use --inspect-candidate-acquisition <context-json> <config> <id> <version> <now> <base-uri> [--option value].");
            var requests = new ProviderRequestLimits(); var graph = new GraphReportLimits(); var limits = new ResolutionAcquisitionLimits();
            using var cancellation = new CancellationTokenSource(); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 7; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate acquisition inspection option.");
                var value = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--max-wire-bytes": requests = requests with { MaximumListingWireBytes = value }; break;
                    case "--max-decode-depth": requests = requests with { Decode = new(MaximumDepth: checked((int)value)) }; break;
                    case "--max-warnings": graph = graph with { MaximumWarnings = checked((int)value) }; break;
                    case "--max-requirements": graph = graph with { MaximumRequirements = checked((int)value) }; break;
                    case "--max-text-characters": graph = graph with { MaximumTextCharacters = checked((int)value) }; break;
                    case "--max-nodes": limits = limits with { MaximumNodes = checked((int)value) }; break;
                    case "--max-domains": limits = limits with { MaximumDomains = checked((int)value) }; break;
                    case "--cancel-after-ms": if (value == 0) cancellation.Cancel(); else cancellation.CancelAfter(checked((int)value)); break;
                    default: throw new ArgumentException("Unknown acquisition inspection option.");
                }
            }
            using var input = File.OpenRead(arguments[1]); if (input.Length > 4 * 1024 * 1024) throw new ArgumentException("Acquisition fixture exceeds4MiB.");
            using var document = JsonDocument.Parse(input, new() { MaxDepth = 256 }); var root = document.RootElement; var descriptor = root.GetProperty("Project");
            var projectRoot = Path.GetFullPath(Text(descriptor, "Root"));
            var project = new ProjectContext(Text(descriptor, "Id", "fixture"), projectRoot, null, null, "fixture.mrpack", Text(descriptor, "MinecraftVersion"),
                Text(descriptor, "Loader"), Text(descriptor, "LoaderVersion"), "index.toml", Path.Combine(projectRoot, "index.toml"));
            var before = ImmutableArray.CreateBuilder<ProjectContentNode>();
            if (root.TryGetProperty("Before", out var observed)) foreach (var entry in observed.EnumerateObject())
            {
                var value = entry.Value; var item = value.GetProperty("Item");
                var kind = Text(item, "Kind") switch { "mod" => ContentKind.Mod, "resourcepack" => ContentKind.ResourcePack, "shaderpack" => ContentKind.ShaderPack, _ => throw new ArgumentException("Unknown observed kind.") };
                ManagedInstallFacts? facts = null;
                if (value.TryGetProperty("InstallFacts", out var install) && install.ValueKind != JsonValueKind.Null)
                    facts = new(null, Flag(install, "Optional"), Flag(install, "DefaultEnabled", true), Flag(install, "Pinned"));
                var owned = new InventoryItem(Text(item, "Id"), kind, Text(item, "Name"), Optional(item, "Filename"), Text(item, "Side"),
                    Text(item, "Source") == "local" ? ContentSource.Local : ContentSource.Packwiz, Optional(item, "VersionId"), Optional(item, "MetadataPath"), facts?.Pinned, Optional(item, "Category"))
                { InstallFacts = facts };
                before.Add(new(owned, new(owned.Id, owned.VersionId ?? "", owned.Name, Text(item, "Kind"), owned.Side, facts?.DefaultEnabled ?? true, [], [], []),
                    null, null, facts?.Pinned ?? false, Text(value, "Intent", "explicit")));
            }
            var session = ResolutionAcquisition.FromObservedNodesAsync(project, before.ToImmutable(), arguments[2], DateTimeOffset.Parse(arguments[5], CultureInfo.InvariantCulture),
                limits with { Observation = new(Graph: graph, Provider: requests) }, cancellation.Token, providerBaseUri: new(arguments[6])).GetAwaiter().GetResult();
            try
            {
                var domain = session.GetCandidatesAsync(arguments[3], cancellation.Token).GetAwaiter().GetResult();
                var afterList = session.ArtifactStatistics.Requests;
                var resolved = session.GetNodeAsync(arguments[3], arguments[4], cancellation.Token).GetAwaiter().GetResult();
                var node = session.GetMaterializedNode(arguments[3], arguments[4]);
                using var output = new OutputBuffer();
                using (var writer = new Utf8JsonWriter(output, new() { MaxDepth = 256 }))
                {
                    writer.WriteStartObject(); writer.WriteString("availability", domain.Availability.ToString());
                    writer.WriteStartArray("versions"); foreach (var id in domain.VersionIds) writer.WriteStringValue(id); writer.WriteEndArray();
                    writer.WriteNumber("artifact_requests_after_list", afterList); writer.WriteNumber("artifact_requests", session.ArtifactStatistics.Requests);
                    writer.WriteNumber("metadata_requests", session.ProviderStatistics.Requests); writer.WritePropertyName("node"); WriteNode(writer, node);
                    if (resolved.OrderingDate is { } date) writer.WriteNumber("ordering_ticks", date.Ticks); else writer.WriteNull("ordering_ticks");
                    writer.WriteEndObject();
                }
                Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check candidate inspection arguments", null); return 2; }
    }

    private static void WriteNode(Utf8JsonWriter writer, ProjectContentNode node)
    {
        var graph = node.Graph; var item = node.Item;
        writer.WriteStartObject(); writer.WriteString("Id", graph.Id); writer.WriteString("VersionId", graph.VersionId);
        writer.WriteString("Intent", node.Intent); writer.WriteBoolean("Pinned", node.Pinned); writer.WriteBoolean("Enabled", graph.Enabled); writer.WriteString("Published", node.Version?.Published.Text ?? "");
        writer.WriteStartObject("Item"); writer.WriteString("Id", item.Id); writer.WriteString("Name", item.Name); writer.WriteString("Kind", graph.Kind); writer.WriteString("Side", item.Side);
        writer.WriteString("Source", item.Source == ContentSource.Local ? "local" : "packwiz"); writer.WriteString("MetadataPath", item.MetadataPath); writer.WriteString("Filename", item.Filename);
        writer.WriteString("VersionId", item.VersionId); writer.WriteString("Category", item.Category); writer.WriteEndObject();
        writer.WritePropertyName("File"); if (node.File is { } file)
        {
            writer.WriteStartObject(); writer.WriteString("Filename", file.Filename); writer.WriteString("Url", file.Url); writer.WriteBoolean("Primary", file.Primary);
            writer.WriteString("Sha1", file.Sha1); writer.WriteString("Sha512", file.Sha512); if (file.Size is { } size) writer.WriteNumber("Size", size); else writer.WriteNull("Size"); writer.WriteEndObject();
        }
        else writer.WriteNullValue();
        writer.WriteStartArray("Requirements"); foreach (var requirement in graph.Requirements) GraphInspection.WriteRequirement(writer, requirement); writer.WriteEndArray();
        writer.WriteStartArray("Warnings"); foreach (var warning in graph.Warnings) writer.WriteStringValue(warning); writer.WriteEndArray();
        writer.WriteStartArray("Mods"); foreach (var mod in graph.Mods)
        {
            writer.WriteStartObject(); writer.WriteString("Id", mod.Declaration.Id); writer.WriteString("Version", mod.Declaration.Version); writer.WriteString("Side", mod.Declaration.Side); writer.WriteBoolean("Nested", mod.Nested);
            writer.WriteStartObject("Provides"); foreach (var alias in mod.Declaration.Provides) if (alias.Key.StringValue is { } key) writer.WriteString(key, alias.Version); writer.WriteEndObject();
            writer.WriteStartArray("Requirements"); foreach (var requirement in mod.Declaration.Requirements) GraphInspection.WriteRequirement(writer, requirement); writer.WriteEndArray(); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }
    private static string Text(JsonElement value, string name, string fallback = "") => Optional(value, name) ?? fallback;
    private static string? Optional(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.Null ? field.GetString() : null;
    private static bool Flag(JsonElement value, string name, bool fallback = false) => value.TryGetProperty(name, out var field) ? field.GetBoolean() : fallback;
    private sealed class OutputBuffer : MemoryStream
    {
        private void Guard(int count)
        { if (count > 64 * 1024 * 1024 - Length) throw new DomainException("Compatibility.InspectionOutputLimit", "Candidate inspection output exceeds64MiB", "inspect fewer candidate facts", "LimitsExceeded", "MaximumOutputBytes"); }
        public override void Write(byte[] buffer, int offset, int count) { Guard(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Guard(buffer.Length); base.Write(buffer); }
    }
}
