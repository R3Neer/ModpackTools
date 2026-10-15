using System.Collections.Immutable;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

namespace ModpackTools.Cli;

internal static class ClassificationCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        var assign = arguments[0].Equals("assign", StringComparison.OrdinalIgnoreCase);
        var values = ImmutableArray.CreateBuilder<string>(); var dryRun = false; var clearAssignments = false;
        foreach (var token in arguments.Skip(1))
        {
            if (!token.StartsWith("--", StringComparison.Ordinal)) { values.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (!new[] { "dry-run", "clear-assignments" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (parts.Length != 1) throw new DomainException("Option.UnexpectedValue", $"Option '--{name}' does not accept a value", $"use --{name} without an attached value", target: name);
            if (name.Equals("dry-run", StringComparison.OrdinalIgnoreCase)) dryRun = true; else clearAssignments = true;
        }
        if (values.Count < (assign ? 2 : 1)) throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", $"modpack category {arguments[0]} <selectors...>");
        if (clearAssignments) throw new DomainException("Option.ForbiddenCombination", "--clear-assignments is only valid for category remove", "modpack category remove --help");
        var effective = ProjectSelection.EffectiveId(requestedProject, activeProject);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effective);
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        var request = new ContentClassificationRequest(values.Skip(assign ? 1 : 0).ToImmutableArray(), assign ? values[0] : "unclassified", dryRun);
        using var output = new TransactionResponse(command, arguments, dryRun, project.Root);
        var result = await ContentClassification.ExecuteAsync(project, request, configurationDirectory, Path.GetFullPath(state), DateTimeOffset.UtcNow,
            cancellation: cancellation, prepareOutput: output.PrepareAsync<string>).ConfigureAwait(false);
        output.Write(result);
    }
}
