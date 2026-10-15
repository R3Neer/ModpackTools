using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Inventory;

namespace ModpackTools.Cli;

internal static class VersionListCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        foreach (var argument in arguments.Skip(1))
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                var name = argument[2..].Split('=', 2)[0];
                throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            }
        if (arguments.Length != 2)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack content versions <selector>",
                targetData: arguments.Length == 1 ? null : new TextListTarget(arguments.Skip(1).ToArray()));
        var effectiveId = ProjectSelection.EffectiveId(requestedProject, activeProject);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effectiveId);
        var view = await CompatibleVersions.ReadForSelectorAsync(project, arguments[1], configurationDirectory, DateTimeOffset.UtcNow,
            cancellation: cancellation).ConfigureAwait(false);
        var kind = view.ItemKind switch
        { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", ContentKind.ShaderPack => "shaderpack", _ => throw new InvalidOperationException("Unknown content kind.") };
        var envelope = new VersionListEnvelope(1, true, command, arguments,
            new(PublicProject.From(project), new(view.ItemName, kind, view.Versions.Select(PublicVersion.From).ToArray())));
        using var output = new NativeOutputBuffer("Version list", "use a smaller provider version listing");
        using (var writer = new Utf8JsonWriter(output)) JsonSerializer.Serialize(writer, envelope, WireJsonContext.Default.VersionListEnvelope);
        cancellation.ThrowIfCancellationRequested();
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
    }
}
