using System.Globalization;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

namespace ModpackTools.Cli;

internal static class CategoryCreationCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        var values = new List<string>(); var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < arguments.Length; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { values.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (!new[] { "name", "order" }.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (parts.Length == 2) options[name] = parts[1];
            else
            {
                if (++index == arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal)) throw new DomainException("Option.MissingValue", $"Option '--{name}' requires a value", $"--{name} <value>", target: name);
                options[name] = arguments[index];
            }
        }
        if (values.Count != 1)
        {
            var bare = values.FirstOrDefault(value => new[] { "name", "order" }.Contains(value, StringComparer.OrdinalIgnoreCase));
            if (bare is not null) throw new DomainException("Option.MissingPrefix", $"Option '{bare}' must start with '--'", $"--{bare}", target: bare);
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack category create <id> [--name <name>] [--order <n>]");
        }
        var effective = ProjectSelection.EffectiveId(requestedProject, activeProject);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effective);
        int? order = null;
        if (options.TryGetValue("order", out var orderText))
        {
            if (!int.TryParse(orderText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed)) throw new DomainException("Option.InvalidOrder", $"Category order '{orderText}' is not an integer", "--order <integer>", target: orderText);
            order = parsed;
        }
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        using var output = new CategoryCreationResponse(command, arguments, project.Root);
        var result = await CategoryCreation.ExecuteAsync(project, new(values[0], options.GetValueOrDefault("name"), order), configurationDirectory,
            Path.GetFullPath(state), DateTimeOffset.UtcNow, cancellation: cancellation, prepareOutput: output.PrepareAsync).ConfigureAwait(false);
        output.Write(result);
    }
}
