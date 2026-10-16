using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Build;
using ModpackTools.Core.Projects;

internal static class BuildInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-build <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 }); var input = document.RootElement;
            bool Flag(string name) => input.TryGetProperty(name, out var value) && value.GetBoolean();
            var project = ProjectReader.Read(input.GetProperty("Root").GetString()!);
            var tool = input.GetProperty("Executable").GetString();
            var now = input.TryGetProperty("Now", out var date) ? DateTimeOffset.Parse(date.GetString()!, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow;
            var result = BuildOperation.ExecuteAsync(project, new(Flag("NoRefresh"), Flag("KeepOld"), Flag("RawLog"), Flag("Strict"), Flag("DryRun")),
                input.GetProperty("ConfigurationDirectory").GetString()!, input.GetProperty("StateDirectory").GetString()!, now,
                () => new(File.Exists(tool), tool, "fixture", null), Environment.ProcessPath!,
                prepareOutput: Flag("FailOutput") ? (_, _) => throw new IOException("fixture output preparation fails") : null).GetAwaiter().GetResult();
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteBoolean("Applied", result.Applied); writer.WriteBoolean("DryRun", result.Result.DryRun);
                writer.WriteString("Path", result.Result.Path); writer.WriteNumber("Size", result.Result.Size); writer.WriteNumber("DurationSeconds", result.Result.Duration.TotalSeconds);
                writer.WriteStartArray("Log"); foreach (var line in result.Result.Log) writer.WriteStringValue(line); writer.WriteEndArray();
                writer.WritePropertyName("Health"); GraphInspection.WriteReport(writer, result.Result.Health);
                MrpackInspection.Records(writer, "ArtifactSnapshot", result.Result.Snapshot);
                writer.WriteStartArray("Changes"); foreach (var change in result.Changes)
                { writer.WriteStartObject(); writer.WriteString("Path", change.Path); writer.WriteString("Before", change.Before); writer.WriteString("After", change.After); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteStartArray("Diagnostics"); foreach (var diagnostic in result.Diagnostics)
                { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Code); writer.WriteString("Path", diagnostic.Path); writer.WriteString("Message", diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check build fixture input", null); return 2; }
    }
}
