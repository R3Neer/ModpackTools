using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

internal static class ProjectLookupInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 7) throw new ArgumentException("Use --inspect-project-lookup <config> <selector> <minecraft> <loader> <now> <base-uri>.");
            return Observe().GetAwaiter().GetResult();
            async Task<int> Observe()
            {
                var root = Path.GetFullPath(args[1]);
                var project = new ProjectContext("fixture", root, null, null, "fixture.mrpack", args[3], args[4], "", "index.toml", Path.Combine(root, "index.toml"));
                await using var source = await ResolutionAcquisition.FromObservedNodesAsync(project, [], root,
                    DateTimeOffset.Parse(args[5], CultureInfo.InvariantCulture), providerBaseUri: new(args[6]));
                var info = await source.LookupProjectAsync(args[2]);
                var domain = await source.GetCandidatesAsync("modrinth:" + info.Id, default);
                using var output = new ProviderCacheInspection.BoundedOutput();
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject(); writer.WriteString("Id", info.Id); writer.WriteString("Title", info.Title); writer.WriteString("RawKind", info.RawKind);
                    writer.WriteString("ClientSide", info.ClientSide); writer.WriteString("ServerSide", info.ServerSide);
                    writer.WriteStartArray("Environments"); foreach (var value in info.Environments) writer.WriteStringValue(value); writer.WriteEndArray();
                    writer.WriteString("Availability", domain.Availability.ToString());
                    writer.WriteStartArray("Versions"); foreach (var value in domain.VersionIds) writer.WriteStringValue(value); writer.WriteEndArray();
                    writer.WriteNumber("MetadataRequests", source.ProviderStatistics.Requests); writer.WriteNumber("ArtifactRequests", source.ArtifactStatistics.Requests); writer.WriteEndObject();
                }
                Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
            }
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check project lookup arguments", null); return 2; }
    }
}
