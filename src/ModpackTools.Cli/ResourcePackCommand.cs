using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal static class ResourcePackCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        var values = ImmutableArray.CreateBuilder<string>(); var dryRun = false; string? positionText = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { values.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (name.Equals("dry-run", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length != 1) throw new DomainException("Option.UnexpectedValue", $"Option '--{name}' does not accept a value", $"use --{name} without an attached value", target: name);
                dryRun = true; continue;
            }
            if (!name.Equals("position", StringComparison.OrdinalIgnoreCase)) throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (parts.Length == 2) positionText = parts[1];
            else
            {
                if (++index == arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    throw new DomainException("Option.MissingValue", $"Option '--{name}' requires a value", $"--{name} <value>", target: name);
                positionText = arguments[index];
            }
        }
        if (values.Count < 2) throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack resource-pack enable|move|disable <selector...> [--position n]");
        var operation = values[0].ToLowerInvariant() switch
        {
            "enable" => ResourcePackOperation.Enable,
            "move" => ResourcePackOperation.Move,
            "disable" => ResourcePackOperation.Disable,
            _ => throw new DomainException("ResourcePack.UnknownOperation", $"Unknown resource-pack operation '{values[0].ToLowerInvariant()}'", "modpack resource-pack --help")
        };
        var position = 0;
        if (operation != ResourcePackOperation.Disable)
        {
            if (positionText is null || !int.TryParse(positionText, NumberStyles.Integer, CultureInfo.CurrentCulture, out position) || position < 1)
                throw new DomainException("Option.InvalidPosition", "A positive --position is required", "--position <n>");
        }
        else if (positionText is not null) throw new DomainException("Option.ForbiddenCombination", "Disable does not accept --position", "remove --position");
        var effective = ProjectSelection.EffectiveId(requestedProject, activeProject);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effective);
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        using var output = new TransactionResponse(command, arguments, dryRun, project.Root);
        var result = await ResourcePackOperationRunner.ExecuteAsync(project, new(operation, values.Skip(1).ToImmutableArray(), position, dryRun),
            configurationDirectory, Path.GetFullPath(state), DateTimeOffset.UtcNow, cancellation: cancellation, prepareOutput: Prepare).ConfigureAwait(false);
        output.Write(result);
        ValueTask Prepare(TransactionPreview<ResourcePackOperationResult> preview, CancellationToken token)
            => output.PrepareAsync(preview, token, dryRun ? null : Rows(preview.Result.Resources));
    }
    internal static PublicInventoryItem[] Rows(ResourcePackView view)
    {
        var rows = new List<PublicInventoryItem>(view.Active.Length + view.Inactive.Length);
        foreach (var item in view.Active) rows.Add(new(null, item.Id, item.Name.Length == 0 ? null : item.Name, "resourcepack", null,
            item.Source.ToString().ToLowerInvariant(), item.Filename, null, false, "active", item.Priority, null));
        foreach (var item in view.Inactive) rows.Add(new(null, item.Id, item.Name.Length == 0 ? null : item.Name, "resourcepack", item.Side,
            item.Source.ToString().ToLowerInvariant(), item.Filename, item.Category, item.Pinned ?? false, "inactive", null, item.VersionId));
        return [.. rows];
    }
}
