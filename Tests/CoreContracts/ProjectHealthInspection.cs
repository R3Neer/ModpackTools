using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;

internal static class ProjectHealthInspection
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length < 4) throw new ArgumentException("Use --inspect-project-health <project-root> <config-root> <now-iso> [options].");
            var now = DateTimeOffset.Parse(args[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var models = new ProviderModelLimits(); var graph = new GraphReportLimits();
            var check = false; Uri? baseUri = null; int? cancelAfter = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 4; index < args.Length; index++)
            {
                var option = args[index];
                if (!seen.Add(option)) throw new ArgumentException("Duplicate health inspection option.");
                if (option == "--check") { check = true; continue; }
                if (++index >= args.Length) throw new ArgumentException("Health option requires a value.");
                if (option == "--provider-base-uri") { baseUri = new(args[index]); continue; }
                var value = long.Parse(args[index], CultureInfo.InvariantCulture);
                switch (option)
                {
                    case "--max-provider-bytes": models = models with { MaximumDecodedBytes = value }; break;
                    case "--max-provider-versions": models = models with { MaximumVersions = checked((int)value) }; break;
                    case "--max-provider-entries": models = models with { MaximumCollectionEntries = checked((int)value) }; break;
                    case "--max-graph-mods": graph = graph with { MaximumMods = checked((int)value) }; break;
                    case "--max-graph-requirements": graph = graph with { MaximumRequirements = checked((int)value) }; break;
                    case "--max-graph-aliases": graph = graph with { MaximumAliases = checked((int)value) }; break;
                    case "--max-graph-warnings": graph = graph with { MaximumWarnings = checked((int)value) }; break;
                    case "--max-graph-text-chars": graph = graph with { MaximumTextCharacters = checked((int)value) }; break;
                    case "--cancel-after-ms": cancelAfter = checked((int)value); break;
                    default: throw new ArgumentException("Unknown health inspection option.");
                }
            }
            using var cancellation = new CancellationTokenSource();
            if (cancelAfter is { } milliseconds) cancellation.CancelAfter(milliseconds);
            var project = ProjectReader.Read(args[1]);
            var result = await ProjectHealth.ReadAsync(project, args[2], now, check, new(Graph: graph, Models: models),
                cancellation.Token, providerBaseUri: baseUri);
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteString("ProjectId", project.Id); writer.WriteBoolean("JavaDeclared", project.JavaDeclared);
                writer.WriteString("JavaVersion", project.JavaVersion); writer.WritePropertyName("Report"); GraphInspection.WriteReport(writer, result.Report);
                writer.WriteStartArray("Nodes");
                foreach (var node in result.Nodes)
                {
                    writer.WriteStartObject(); writer.WriteString("Id", node.Graph.Id); writer.WriteString("Name", node.Graph.Name);
                    writer.WriteString("VersionId", node.Graph.VersionId); writer.WriteString("Kind", node.Graph.Kind); writer.WriteString("Side", node.Graph.Side);
                    writer.WriteBoolean("Enabled", node.Graph.Enabled); writer.WriteBoolean("Pinned", node.Pinned); writer.WriteString("Intent", node.Intent);
                    writer.WritePropertyName("Version"); if (node.Version is null) writer.WriteNullValue(); else ProviderCacheInspection.WriteVersion(writer, node.Version);
                    writer.WritePropertyName("File");
                    if (node.File is not { } file) writer.WriteNullValue();
                    else { writer.WriteStartObject(); writer.WriteString("Filename", file.Filename); writer.WriteString("Url", file.Url); writer.WriteBoolean("Primary", file.Primary); writer.WriteString("Sha1", file.Sha1); writer.WriteString("Sha512", file.Sha512); writer.WriteEndObject(); }
                    writer.WriteStartArray("Mods"); foreach (var mod in node.Graph.Mods)
                    { writer.WriteStartObject(); writer.WriteString("Id", mod.Declaration.Id); writer.WriteString("Version", mod.Declaration.Version); writer.WriteString("Side", mod.Declaration.Side); writer.WriteBoolean("Nested", mod.Nested); writer.WriteEndObject(); }
                    writer.WriteEndArray(); writer.WriteStartArray("Requirements"); foreach (var requirement in node.Graph.Requirements) GraphInspection.WriteRequirement(writer, requirement);
                    writer.WriteEndArray(); writer.WriteStartArray("Warnings"); foreach (var warning in node.Graph.Warnings) writer.WriteStringValue(warning);
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteStartObject("ProviderAvailability");
                foreach (var (id, observed) in result.ProviderAvailability)
                { writer.WriteStartObject(id); writer.WriteString("Availability", observed.Availability.ToString()); writer.WriteString("Endpoint", observed.Endpoint); writer.WriteEndObject(); }
                writer.WriteEndObject(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, (int)output.Length); return 0;
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check project health inspection arguments", null); return 2; }
    }
}
