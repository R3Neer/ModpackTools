using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

namespace ModpackTools.Cli;

internal static class PinCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        var selectors = ImmutableArray.CreateBuilder<string>(); var dryRun = false;
        foreach (var token in arguments.Skip(1))
        {
            if (!token.StartsWith("--", StringComparison.Ordinal)) { selectors.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (!name.Equals("dry-run", StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (parts.Length != 1) throw new DomainException("Option.UnexpectedValue", "Option '--dry-run' does not accept a value", "use --dry-run without an attached value", target: name);
            dryRun = true;
        }
        if (selectors.Count == 0) throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", $"modpack content {arguments[0]} <selector...>");
        var effective = ProjectSelection.EffectiveId(requestedProject, activeProject);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effective);
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        using var output = new TransactionResponse(command, arguments, dryRun, project.Root);
        var result = await ContentPinning.ExecuteAsync(project, new(selectors.ToImmutable(), arguments[0].Equals("pin", StringComparison.OrdinalIgnoreCase), dryRun),
            configurationDirectory, Path.GetFullPath(state), DateTimeOffset.UtcNow, ResolveTool, Environment.ProcessPath!, cancellation: cancellation,
            prepareOutput: output.PrepareAsync<ImmutableArray<string>>).ConfigureAwait(false);
        output.Write(result);

        PackwizObservation ResolveTool() => PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
            Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68");
    }
}
