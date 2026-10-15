using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

namespace ModpackTools.Cli;

internal static class ContentMutationCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        var request = Parse(arguments);
        var effectiveId = ProjectSelection.EffectiveId(requestedProject, activeProject);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effectiveId);
        // Advanced host bootstrap namespace, kept stable across configuration/cache
        // relocations. Installed launchers will retain this value across updates.
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        using var output = new TransactionResponse(command, arguments, request.DryRun, project.Root);
        var result = await ContentOperation.ExecuteAsync(project, request, configurationDirectory, Path.GetFullPath(state), DateTimeOffset.UtcNow,
            ResolveTool, Environment.ProcessPath!, cancellation: cancellation, prepareOutput: output.PrepareAsync<ContentPlan>).ConfigureAwait(false);
        output.Write(result);
        PackwizObservation ResolveTool()
        {
            var configured = ConfigurationReader.ReadPackwizPath(configurationDirectory);
            return PackwizRefresh.Resolve(configured, Environment.GetEnvironmentVariable("PATH"), Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68");
        }
    }

    internal static ContentOperationRequest Parse(string[] arguments)
    {
        var operation = arguments[0].ToLowerInvariant() switch
        { "add" => ContentPlanOperation.Add, "update" => ContentPlanOperation.Update, _ => throw new ArgumentException("Unknown mutation.") };
        var values = operation == ContentPlanOperation.Add ? new[] { "category", "enable-at" } : operation == ContentPlanOperation.Update ? ["type", "to"] : Array.Empty<string>();
        var switches = operation == ContentPlanOperation.Update ? new[] { "all", "dry-run", "strict", "allow-downgrade" } : ["dry-run", "strict", "allow-downgrade"];
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); var positionals = new List<string>();
        for (var index = 1; index < arguments.Length; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { positionals.Add(token); continue; }
            var pair = token[2..].Split('=', 2); var name = pair[0]; var attached = pair.Length == 2 ? pair[1] : null;
            if (switches.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (attached is not null) throw Error("UnexpectedValue", $"Option '--{name}' does not accept a value", $"use --{name} without an attached value", name);
                options[name] = null; continue;
            }
            if (!values.Contains(name, StringComparer.OrdinalIgnoreCase)) throw Error("Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", name);
            if (attached is null)
            {
                if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal)) throw Error("MissingValue", $"Option '--{name}' requires a value", $"--{name} <value>", name);
                attached = arguments[index];
            }
            options[name] = attached;
        }
        if (operation == ContentPlanOperation.Add && positionals.Count == 0)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack content " + arguments[0] + (operation == ContentPlanOperation.Add ? " <selector...> [options]" : " [options]"),
                targetData: positionals.Count == 0 ? null : new TextListTarget([.. positionals]));
        if (operation == ContentPlanOperation.Add && positionals.Contains("category", StringComparer.InvariantCultureIgnoreCase))
            throw Error("MissingPrefix", "Option 'category' must start with '--'", "--category");
        if (operation == ContentPlanOperation.Update && (options.ContainsKey("all") ? positionals.Count != 0 : positionals.Count == 0))
            throw Error("ForbiddenCombination", "Specify selectors or --all", "modpack content update --help");
        if (operation == ContentPlanOperation.Update && options.ContainsKey("to") && (options.ContainsKey("all") || positionals.Count != 1))
            throw Error("VersionTargetConflict", "--to requires exactly one selector", "modpack content update <selector> --to <version>");
        int? enableAt = null;
        if (options.TryGetValue("enable-at", out var enable))
        {
            if (!int.TryParse(enable, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position) || position < 1)
                throw Error("InvalidPosition", "Enable position must be a positive integer", "--enable-at <n>");
            enableAt = position;
        }
        return new(new(operation, positionals.ToImmutableArray(), options.ContainsKey("all"), options.GetValueOrDefault("type") ?? "all",
            options.GetValueOrDefault("to"), options.ContainsKey("strict"), options.ContainsKey("allow-downgrade")),
            options.ContainsKey("dry-run"), options.GetValueOrDefault("category"), enableAt);
    }
    private static DomainException Error(string id, string message, string hint, string? target = null) => new("Option." + id, message, hint, target: target);
}
