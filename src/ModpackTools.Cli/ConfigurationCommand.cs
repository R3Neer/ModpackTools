using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Processes;

namespace ModpackTools.Cli;

internal static class ConfigurationCommand
{
    private const string Usage = "modpack config get <root|packwiz> | modpack config set <root|packwiz> <value>";
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string configurationDirectory, CancellationToken cancellation, bool includePresentation = false)
    {
        if (requestedProject is not null) throw new DomainException("Option.ForbiddenCombination", "Option '--project' is not valid for this operation", "modpack config --help", target: requestedProject);
        if (arguments.Length is < 2 or > 3) throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", Usage,
            targetData: arguments.Length == 1 ? new TextTarget(arguments[0]) : new TextListTarget(arguments));
        var verb = arguments[0]; var name = arguments[1].ToLowerInvariant();
        if (name is not ("root" or "packwiz")) throw new DomainException("Configuration.UnknownSetting", $"Configuration setting '{name}' is not recognized; allowed values: root, packwiz", "modpack config --help", target: name);
        if (verb.Equals("get", StringComparison.OrdinalIgnoreCase) && arguments.Length != 2
            || verb.Equals("set", StringComparison.OrdinalIgnoreCase) && arguments.Length != 3)
            throw new DomainException("Command.InvalidArguments", $"The arguments for 'config {verb.ToLowerInvariant()}' do not match the expected syntax", "modpack config --help");
        if (!new[] { "get", "set" }.Contains(verb, StringComparer.OrdinalIgnoreCase)) throw new DomainException("Configuration.UnknownOperation", $"Configuration operation '{verb}' is not recognized; allowed values: get, set", "modpack config --help", target: verb);
        configurationDirectory = Path.GetFullPath(configurationDirectory);
        using var output = new NativeOutputBuffer("Configuration", "reduce configuration arguments");
        using (var writer = new Utf8JsonWriter(output)) JsonSerializer.Serialize(writer,
            new EmptySuccessEnvelope(1, true, command, arguments, new()), WireJsonContext.Default.EmptySuccessEnvelope);
        cancellation.ThrowIfCancellationRequested();
        if (verb.Equals("set", StringComparison.OrdinalIgnoreCase))
            _ = await ConfigurationStore.SetAsync(configurationDirectory, new(name == "root" ? ConfigurationSetting.Root : ConfigurationSetting.Packwiz, arguments[2]), cancellation).ConfigureAwait(false);
        else
        {
            ConfigurationPresentation? presentation;
            if (name == "root") presentation = new("configuration", name, ConfigurationReader.ReadRoot(configurationDirectory), null);
            else
            {
                var tool = PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
                    Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68");
                presentation = new("configuration", name, tool.Available ? tool.Path! : "Not found", tool.Source);
            }
            cancellation.ThrowIfCancellationRequested();
            if (includePresentation)
            {
                output.SetLength(0); output.Position = 0;
                using var writer = new Utf8JsonWriter(output);
                JsonSerializer.Serialize(writer, new ConfigurationEnvelope(1, true, command, arguments, new(), presentation), WireJsonContext.Default.ConfigurationEnvelope);
            }
        }
        // The original machine contract is data={}; prepare its entire bytes
        // before a set. Human key/value presentation is a separate adapter concern.
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
    }
}
