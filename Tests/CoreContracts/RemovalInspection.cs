using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

internal static class RemovalInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-removal <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 4 * 1024 * 1024) throw new ArgumentException("Request exceeds 4 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 32 }); var input = document.RootElement;
            bool Flag(string name) => input.TryGetProperty(name, out var value) && value.GetBoolean();
            var project = ProjectReader.Read(input.GetProperty("Root").GetString()!);
            var planning = new RemovalPlanRequest([.. input.GetProperty("Selectors").EnumerateArray().Select(value => value.GetString()!)],
                input.TryGetProperty("Type", out var type) ? type.GetString()! : "all", Flag("Cascade"), Flag("AutoRemove"), Flag("Strict"));
            var now = input.TryGetProperty("Now", out var date) ? DateTimeOffset.Parse(date.GetString()!, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow;
            var tool = input.GetProperty("Executable").GetString();
            ModpackTools.Core.Transactions.TransactionExpectation? expected = null;
            if (Flag("InvalidatePreview"))
            {
                var preview = Execute(true, false);
                File.AppendAllText(Path.Combine(project.Root, "pack.toml"), "\n# fixture edit after reviewed preview\n"); expected = preview.Expectation;
            }
            var result = Execute(Flag("DryRun"), Flag("FailOutput"));
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteBoolean("Applied", result.Applied);
                writer.WriteStartArray("Requested"); foreach (var id in result.Result.Requested) writer.WriteStringValue(id); writer.WriteEndArray();
                writer.WriteStartObject("Nodes"); foreach (var node in result.Result.Nodes) { writer.WritePropertyName(node.Graph.Id); CandidateAcquisitionInspection.WriteNode(writer, node); }
                writer.WriteEndObject();
                writer.WriteStartArray("Removed"); foreach (var change in result.Result.Changes)
                { writer.WriteStartObject(); writer.WriteString("Id", change.Id); writer.WriteString("Reason", change.Reason); writer.WritePropertyName("Before"); CandidateAcquisitionInspection.WriteNode(writer, change.Before); writer.WriteNull("After"); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WritePropertyName("Baseline"); GraphInspection.WriteReport(writer, result.Result.Baseline); writer.WritePropertyName("Report"); GraphInspection.WriteReport(writer, result.Result.Report);
                writer.WriteStartArray("Changes"); foreach (var change in result.Changes)
                { writer.WriteStartObject(); writer.WriteString("Path", change.Path); writer.WriteString("Before", change.Before); writer.WriteString("After", change.After); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteStartArray("Diagnostics"); foreach (var diagnostic in result.Diagnostics)
                { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Code); writer.WriteString("Path", diagnostic.Path); writer.WriteString("Message", diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
            ModpackTools.Core.Transactions.TransactionResult<RemovalPlan> Execute(bool dry, bool fail) => RemovalOperation.ExecuteAsync(project, new(planning, dry),
                input.GetProperty("ConfigurationDirectory").GetString()!, input.GetProperty("StateDirectory").GetString()!, now,
                () => new(File.Exists(tool), tool, "fixture", null), Environment.ProcessPath!, expected: expected,
                prepareOutput: fail ? (_, _) => throw new IOException("fixture output preparation fails") : null).GetAwaiter().GetResult();
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check removal fixture input", null); return 2; }
    }
}
