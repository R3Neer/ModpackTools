using System.Diagnostics;
using ModpackTools.Core;
using ModpackTools.Core.Build;
using ModpackTools.Core.Data;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal static class BuildCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation, bool includePresentation = false)
    {
        var switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var values = new List<string>();
        foreach (var token in arguments)
        {
            if (!token.StartsWith("--", StringComparison.Ordinal)) { values.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (!new[] { "no-refresh", "keep-old", "open", "raw-log", "strict", "dry-run" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (parts.Length != 1) throw new DomainException("Option.UnexpectedValue", $"Option '--{name}' does not accept a value", $"use --{name} without an attached value", target: name);
            switches.Add(name);
        }
        if (values.Count != 0)
        {
            var bare = values.FirstOrDefault(value => new[] { "no-refresh", "keep-old", "open", "raw-log" }.Contains(value, StringComparer.OrdinalIgnoreCase));
            if (bare is not null) throw new DomainException("Option.MissingPrefix", $"Option '{bare}' must start with '--'", $"--{bare}", target: bare);
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack build [options]",
                targetData: values.Count == 1 ? new TextTarget(values[0]) : new TextListTarget([.. values]));
        }
        var effective = ProjectSelection.EffectiveId(requestedProject, activeProject); configurationDirectory = Path.GetFullPath(configurationDirectory);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effective);
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        var dryRun = switches.Contains("dry-run"); using var output = new BuildResponse(command, arguments, project.Root, includePresentation);
        var result = await BuildOperation.ExecuteAsync(project, new(switches.Contains("no-refresh"), switches.Contains("keep-old"), switches.Contains("raw-log"), switches.Contains("strict"), dryRun),
            configurationDirectory, Path.GetFullPath(state), DateTimeOffset.UtcNow, ResolveTool, Environment.ProcessPath!, cancellation: cancellation, prepareOutput: output.PrepareAsync).ConfigureAwait(false);
        if (switches.Contains("open") && !dryRun)
        {
            try { using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false, ArgumentList = { "/select," + result.Result.Path } }); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            { result = result with { Diagnostics = result.Diagnostics.Add(new TransactionDiagnostic("Build.OpenFailed", result.Result.Path, error.Message)) }; }
        }
        output.Write(result);
        PackwizObservation ResolveTool() => PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
            Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68");
    }
}
