using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

namespace ModpackTools.Cli;

internal static class ModSideCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        var values = new List<string>(); var dryRun = false;
        foreach (var token in arguments)
        {
            if (!token.StartsWith("--", StringComparison.Ordinal)) { values.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (!name.Equals("dry-run", StringComparison.OrdinalIgnoreCase)) throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (parts.Length != 1) throw new DomainException("Option.UnexpectedValue", $"Option '--{name}' does not accept a value", $"use --{name} without an attached value", target: name);
            dryRun = true;
        }
        if (values.Count < 3) throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack mod set-side <client|host|both> <selector...>",
            targetData: values.Count switch { 0 => null, 1 => new TextTarget(values[0]), _ => new TextListTarget(values.ToArray()) });
        if (!values[0].Equals("set-side", StringComparison.OrdinalIgnoreCase)) throw new DomainException("Content.UnknownModOperation", $"Mod operation '{values[0]}' is not recognized", "modpack mod --help");
        var effective = ProjectSelection.EffectiveId(requestedProject, activeProject);
        configurationDirectory = Path.GetFullPath(configurationDirectory);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effective);
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        using var output = new TransactionResponse(command, arguments, dryRun, project.Root);
        var result = await ModSideOperation.ExecuteAsync(project, new([.. values.Skip(2)], values[1], dryRun), configurationDirectory, Path.GetFullPath(state), DateTimeOffset.UtcNow,
            ResolveTool, Environment.ProcessPath!, cancellation: cancellation, prepareOutput: output.PrepareAsync<string>).ConfigureAwait(false);
        output.Write(result);
        PackwizObservation ResolveTool() => PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
            Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68");
    }
}
