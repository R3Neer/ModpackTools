using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Providers;

internal static class ProviderCandidateInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length is not (6 or 8) || args.Length == 8 && args[6] != "--base-uri")
                throw new ArgumentException("Use --inspect-provider-candidates <config> <project-id> <minecraft> <loader> <now-iso> [--base-uri URI].");
            var now = DateTimeOffset.Parse(args[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return Observe().GetAwaiter().GetResult();
            async Task<int> Observe()
            {
                await using var client = args.Length == 8 ? new ModrinthVersionClient(args[1], now, baseUri: new Uri(args[7])) : null;
                var result = await ProviderCandidates.ObserveAsync(args[1], args[2], args[3], args[4], now, client);
                using var output = new ProviderCacheInspection.BoundedOutput();
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject(); writer.WriteString("Availability", result.Availability.ToString());
                    writer.WriteString("ProjectStatus", result.ProjectStatus.ToString()); writer.WriteString("ListStatus", result.ListStatus?.ToString());
                    writer.WriteString("Endpoint", result.Endpoint); writer.WriteBoolean("Checked", result.Checked);
                    writer.WriteString("ProjectSource", result.ProjectSource.ToString());
                    Evidence("ProjectCache", result.ProjectCache); Evidence("ListCache", result.ListCache);
                    writer.WritePropertyName("Project");
                    if (result.Project is not { } project) writer.WriteNullValue();
                    else
                    {
                        writer.WriteStartObject(); writer.WriteString("Id", project.Id); writer.WriteString("Title", project.Title);
                        writer.WriteString("RawKind", project.RawKind); writer.WriteString("Kind", project.Kind.ToString());
                        writer.WriteString("ClientSide", project.ClientSide); writer.WriteString("ServerSide", project.ServerSide);
                        writer.WriteStartArray("Environments"); foreach (var value in project.Environments) writer.WriteStringValue(value);
                        writer.WriteEndArray(); writer.WriteEndObject();
                    }
                    writer.WriteStartArray("Versions"); foreach (var version in result.Versions) ProviderCacheInspection.WriteVersion(writer, version);
                    writer.WriteEndArray(); writer.WriteEndObject();
                    void Evidence(string name, ProviderCandidateEvidence? evidence)
                    {
                        writer.WritePropertyName(name);
                        if (evidence is null) { writer.WriteNullValue(); return; }
                        writer.WriteStartObject(); writer.WriteString("Path", evidence.Path); writer.WriteString("Status", evidence.Status.ToString());
                        writer.WriteString("ModifiedUtc", evidence.ModifiedUtc?.ToString("o", CultureInfo.InvariantCulture)); writer.WriteEndObject();
                    }
                }
                Console.OpenStandardOutput().Write(output.GetBuffer(), 0, (int)output.Length); return 0;
            }
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check candidate inspection arguments", null); return 2; }
    }
}
