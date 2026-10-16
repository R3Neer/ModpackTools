using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

namespace ModpackTools.Cli;

internal static class RemovalCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation, bool includePresentation = false, string? reviewToken = null)
    {
        var values = new List<string>(); var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < arguments.Length; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { values.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (new[] { "cascade", "autoremove", "strict", "dry-run", "yes" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (parts.Length != 1) throw Error("UnexpectedValue", $"Option '--{name}' does not accept a value", $"use --{name} without an attached value", name);
                options[name] = null; continue;
            }
            if (!name.Equals("type", StringComparison.OrdinalIgnoreCase)) throw Error("Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", name);
            var value = parts.Length == 2 ? parts[1] : null;
            if (value is null)
            {
                if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal)) throw Error("MissingValue", $"Option '--{name}' requires a value", $"--{name} <value>", name);
                value = arguments[index];
            }
            options[name] = value;
        }
        if (values.Count == 0) throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack content remove <selector...> [options]");
        var effective = ProjectSelection.EffectiveId(requestedProject, activeProject);
        configurationDirectory = Path.GetFullPath(configurationDirectory);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effective);
        var type = options.GetValueOrDefault("type") ?? "all"; var normalized = InventorySelector.NormalizeType(type);
        var now = DateTimeOffset.UtcNow; var kinds = normalized switch
        { InventoryType.Mod => new[] { "mod" }, InventoryType.ResourcePack => ["resourcepack"], InventoryType.ShaderPack => ["shaderpack"], _ => ["mod", "resourcepack", "shaderpack"] };
        var selectors = BatchSelectorResolver.ResolveFromDirectory([.. values], configurationDirectory, project.Id, now, new(kinds));
        selectors = RemovalSelector.Resolve(InventoryReader.Read(project), selectors, type);
        var planning = new RemovalPlanRequest(selectors, type, options.ContainsKey("cascade"), options.ContainsKey("autoremove"), options.ContainsKey("strict"));
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        state = Path.GetFullPath(state); var dryRun = options.ContainsKey("dry-run");
        using var previewOutput = new TransactionResponse(command, arguments, dryRun, project.Root, isPreview: !dryRun);
        var preview = await RemovalOperation.ExecuteAsync(project, new(planning, true), configurationDirectory, state, now, ResolveTool, Environment.ProcessPath!,
            cancellation: cancellation, prepareOutput: includePresentation ? previewOutput.PrepareRemovalAsync : previewOutput.PrepareAsync<RemovalPlan>).ConfigureAwait(false);
        if (reviewToken is not null)
            RemovalPresentation.From(new(preview.Result, preview.Changes, preview.Expectation)).AssertReviewed(reviewToken);
        // Machine mode never reads terminal input. Explicit --yes authorizes the
        // reviewed, source-bound application; otherwise return its safe preview.
        if (dryRun || preview.Changes.IsEmpty || !options.ContainsKey("yes")) { previewOutput.Write(preview); return; }
        using var output = new TransactionResponse(command, arguments, false, project.Root);
        var result = await RemovalOperation.ExecuteAsync(project, new(planning), configurationDirectory, state, now, ResolveTool, Environment.ProcessPath!,
            expected: preview.Expectation, cancellation: cancellation, prepareOutput: includePresentation ? output.PrepareRemovalAsync : output.PrepareAsync<RemovalPlan>).ConfigureAwait(false);
        output.Write(result);
        PackwizObservation ResolveTool() => PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
            Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68");
    }
    private static DomainException Error(string id, string message, string hint, string? target) => new("Option." + id, message, hint, target: target);
}
