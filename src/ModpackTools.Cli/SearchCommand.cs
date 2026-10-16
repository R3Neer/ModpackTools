using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;

namespace ModpackTools.Cli;

internal static class SearchCommand
{
    private const string Usage = "modpack content search <query> [--type <type>] [--limit <1-50>]";

    public static async Task RunAsync(string command, string[] arguments, string? projectId, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();
        for (var index = 1; index < arguments.Length; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { positionals.Add(token); continue; }
            var equals = token.IndexOf('='); var name = equals < 0 ? token[2..] : token[2..equals];
            if (!name.Equals("type", StringComparison.OrdinalIgnoreCase) && !name.Equals("limit", StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            string value;
            if (equals >= 0) value = token[(equals + 1)..];
            else
            {
                if (++index == arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    throw new DomainException("Option.MissingValue", $"Option '--{name}' requires a value", $"--{name} <value>", target: name);
                value = arguments[index];
            }
            values[name] = value;
        }
        if (positionals.Count is < 1 or > 100)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", Usage,
                targetData: positionals.Count == 1 ? new TextTarget(positionals[0]) : positionals.Count == 0 ? null : new TextListTarget(positionals.ToArray()));
        ProjectContext? project = null;
        var selected = string.IsNullOrEmpty(projectId) ? activeProject : projectId;
        if (!string.IsNullOrEmpty(selected))
        {
            var effectiveId = ProjectSelection.EffectiveId(selected, activeProject);
            project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effectiveId);
        }
        var limit = 10;
        if (values.TryGetValue("limit", out var rawLimit) && (!int.TryParse(rawLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50))
            throw new DomainException("Option.InvalidLimit", $"Option '--limit' must be an integer from 1 through 50; received '{rawLimit}'", "--limit <1-50>", target: rawLimit);
        var query = string.Join(' ', positionals);
        var view = await ProviderSearch.ReadAsync(query, values.GetValueOrDefault("type", "all"), limit, project, configurationDirectory,
            DateTimeOffset.UtcNow, cancellation: cancellation).ConfigureAwait(false);
        var envelope = new SearchEnvelope(1, true, command, arguments, new(project is null ? null : PublicProject.From(project),
            new(view.Query, view.Type, view.Results.TotalHits, view.Results.Items.Select(PublicSearchHit.From).ToArray())));
        using var output = new NativeOutputBuffer("Search results", "use a smaller search result limit");
        using (var writer = new Utf8JsonWriter(output)) JsonSerializer.Serialize(writer, envelope, WireJsonContext.Default.SearchEnvelope);
        cancellation.ThrowIfCancellationRequested();
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
    }
}
