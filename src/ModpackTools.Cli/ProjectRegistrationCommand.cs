using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;

namespace ModpackTools.Cli;

internal static class ProjectRegistrationCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string configurationDirectory, CancellationToken cancellation, bool includePresentation = false)
    {
        if (requestedProject is not null) throw new DomainException("Option.ForbiddenCombination", "Option '--project' is not valid for this operation", "modpack project register --help", target: requestedProject);
        string[] names = ["path", "display-name", "display-version", "output-name"];
        var values = new List<string>(); var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < arguments.Length; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { values.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (parts.Length == 2) options[name] = parts[1];
            else
            {
                if (++index == arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal)) throw new DomainException("Option.MissingValue", $"Option '--{name}' requires a value", $"--{name} <value>", target: name);
                options[name] = arguments[index];
            }
        }
        if (values.Count != 1)
        {
            var bare = values.FirstOrDefault(value => names.Contains(value, StringComparer.OrdinalIgnoreCase));
            if (bare is not null) throw new DomainException("Option.MissingPrefix", $"Option '{bare}' must start with '--'", $"--{bare}", target: bare);
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack project register <id> [--path <directory>] [options]",
                targetData: values.Count == 0 ? null : new TextListTarget(values.ToArray()));
        }
        // Root lookup belongs after grammar, and global active project is irrelevant.
        var request = new ProjectRegistrationRequest(values[0], options.GetValueOrDefault("path") ?? Environment.CurrentDirectory,
            options.GetValueOrDefault("display-name"), options.GetValueOrDefault("display-version"), options.GetValueOrDefault("output-name"));
        ProjectRegistration.ValidateRequest(request);
        var root = ConfigurationReader.ReadRoot(Path.GetFullPath(configurationDirectory));
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        using var output = new ProjectRegistrationResponse(command, arguments, root, includePresentation);
        var result = await ProjectRegistration.ExecuteAsync(root, request, Path.GetFullPath(state), cancellation,
            prepareOutput: output.PrepareAsync).ConfigureAwait(false);
        output.Write(result);
    }
}
