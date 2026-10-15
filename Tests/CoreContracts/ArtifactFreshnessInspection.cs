using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Build;
using ModpackTools.Core.Projects;

internal static class ArtifactFreshnessInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-artifact-freshness <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 }); var input = document.RootElement;
            var project = ProjectReader.Read(input.GetProperty("Root").GetString()!); var tool = input.GetProperty("Executable").GetString();
            using var cancellation = new CancellationTokenSource(); if (input.TryGetProperty("Cancelled", out var cancelled) && cancelled.GetBoolean()) cancellation.Cancel();
            var result = ArtifactFreshness.ReadAsync(project, input.GetProperty("ConfigurationDirectory").GetString()!, input.GetProperty("StateDirectory").GetString()!,
                () => new(File.Exists(tool), tool, "fixture", null), Environment.ProcessPath!, cancellation: cancellation.Token).GetAwaiter().GetResult();
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteString("State", result.State); writer.WriteString("Path", result.Path);
                if (result.BuiltUtc is { } built) writer.WriteString("BuiltUtc", built.ToString("O", CultureInfo.InvariantCulture)); else writer.WriteNull("BuiltUtc");
                writer.WriteNumber("Total", result.Comparison.Total); writer.WriteString("Error", result.Error); writer.WriteBoolean("FromCache", result.FromCache);
                MrpackInspection.Records(writer, "Added", result.Comparison.Added); MrpackInspection.Records(writer, "Removed", result.Comparison.Removed);
                writer.WriteStartArray("Changed"); foreach (var item in result.Comparison.Changed)
                { writer.WriteStartObject(); writer.WriteString("Key", item.Key); writer.WriteString("Kind", item.Kind); writer.WriteString("Path", item.Path); writer.WriteString("Before", item.Before); writer.WriteString("After", item.After); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check artifact freshness input", null); return 2; }
    }
}
