using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;

namespace ModpackTools.Cli;

internal static class ProjectCreationCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string configurationDirectory, CancellationToken cancellation)
    {
        if (requestedProject is not null) throw new DomainException("Option.ForbiddenCombination", "Option '--project' is not valid for this operation", "modpack project create --help", target: requestedProject);
        string[] names = ["name", "minecraft", "loader", "path", "loader-version", "pack-version", "display-version"];
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
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack project create <id> --name <name> --minecraft <version> --loader <fabric|quilt|forge|neoforge>",
                targetData: values.Count == 0 ? null : new TextListTarget(values.ToArray()));
        }
        foreach (var required in new[] { "name", "minecraft", "loader" })
            if (!options.ContainsKey(required)) throw new DomainException("Option.Required", $"Required option '--{required}' is missing", "modpack project --help", target: required);
        var request = new ProjectCreationRequest(values[0], options["name"], options["minecraft"], options["loader"], options.GetValueOrDefault("loader-version"),
            options.GetValueOrDefault("path"), options.GetValueOrDefault("pack-version") ?? "0.1.0", options.GetValueOrDefault("display-version"));
        ProjectCreation.ValidateRequest(request);
        configurationDirectory = Path.GetFullPath(configurationDirectory);
        var root = ConfigurationReader.ReadRoot(configurationDirectory);
        using var output = new NativeOutputBuffer("Project creation", "reduce creation arguments");
        using (var writer = new Utf8JsonWriter(output)) JsonSerializer.Serialize(writer,
            new EmptySuccessEnvelope(1, true, command, arguments, new()), WireJsonContext.Default.EmptySuccessEnvelope);
        _ = await ProjectCreation.ExecuteAsync(root, request, new(false, null, "unresolved", null), Environment.ProcessPath!, cancellation: cancellation,
            resolveTool: () => PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
                Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68")).ConfigureAwait(false);
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
    }
}
