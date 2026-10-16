using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Build;
using ModpackTools.Core.Projects;

internal static class ProjectIndexInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-project-index <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 }); var input = document.RootElement;
            var limits = new ProjectIndexLimits();
            if (input.TryGetProperty("Limits", out var policy)) limits = new(Number("MaximumRows", limits.MaximumRows), Number("MaximumEntries", limits.MaximumEntries), Number("MaximumDirectories", limits.MaximumDirectories),
                Long("MaximumHashedBytes", limits.MaximumHashedBytes), Number("MaximumDocumentBytes", limits.MaximumDocumentBytes), Number("MaximumIssues", limits.MaximumIssues),
                Number("MaximumDetailCharacters", limits.MaximumDetailCharacters), Long("MaximumPathCharacters", limits.MaximumPathCharacters));
            using var cancellation = new CancellationTokenSource(); if (input.TryGetProperty("Cancelled", out var cancelled) && cancelled.GetBoolean()) cancellation.Cancel();
            var report = ProjectIndex.Read(ProjectReader.Read(input.GetProperty("Root").GetString()!), limits, cancellation.Token);
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteBoolean("Valid", report.Valid); writer.WriteNumber("Rows", report.Rows);
                writer.WriteStartArray("Issues"); foreach (var issue in report.Issues) writer.WriteStringValue(issue); writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
            int Number(string name, int fallback) => policy.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
            long Long(string name, long fallback) => policy.TryGetProperty(name, out var value) ? value.GetInt64() : fallback;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check project index fixture input", null); return 2; }
    }
}
