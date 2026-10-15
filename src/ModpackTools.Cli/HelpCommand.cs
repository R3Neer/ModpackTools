using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core;

namespace ModpackTools.Cli;

internal sealed record HelpItem(string Label, string Description);
internal sealed record HelpEntry(string Name, string Group, string Summary, string Description,
    string[] Usage, HelpItem[] Items, string[] Notes, string[] Examples);
internal sealed record HelpCatalogue(string Product, string Version, string Description, string Invocation,
    string[] Groups, string[] Usage, [property: JsonPropertyName("global-items")] HelpItem[] GlobalItems,
    string[] Notes, HelpEntry[] Commands);
internal sealed record HelpSelection(string? Command);
internal sealed record HelpData([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HelpSelection? Help);
internal sealed record HelpPresentation(HelpCatalogue Help, string Topic);
internal sealed record HelpEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, HelpData Data, HelpPresentation Presentation);

internal static class HelpCommand
{
    public static bool Applies(string command, string[] arguments) => command.Length == 0 || command.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || arguments.Contains("--help", StringComparer.OrdinalIgnoreCase);

    public static void Run(string command, string[] arguments)
    {
        using var resource = typeof(HelpCommand).Assembly.GetManifestResourceStream("ModpackTools.HelpCatalogue")
            ?? throw new InvalidOperationException("Embedded help catalogue is missing.");
        var catalogue = JsonSerializer.Deserialize(resource, WireJsonContext.Default.HelpCatalogue)
            ?? throw new InvalidOperationException("Embedded help catalogue is invalid.");
        catalogue = catalogue with { Version = ProductVersion.Current.Version };
        var global = command.Length == 0 || command.Equals("--help", StringComparison.OrdinalIgnoreCase);
        if (global && arguments.Length != 0)
            throw new DomainException("Command.InvalidArguments", "The global help option does not accept additional arguments", "modpack --help",
                targetData: arguments.Length == 1 ? new TextTarget(arguments[0]) : new TextListTarget(arguments));
        var topic = global ? "" : command.ToLowerInvariant();
        if (!global && !catalogue.Commands.Any(entry => entry.Name == topic))
            throw new DomainException("Command.Unknown", $"Command '{command}' is not recognized", "modpack --help", target: command);
        Validate(catalogue);
        var result = new HelpEnvelope(1, true, command, arguments, new(global ? new(null) : null), new(catalogue, topic));
        using var output = new NativeOutputBuffer("Help", "use the packaged command catalogue");
        JsonSerializer.Serialize(output, result, WireJsonContext.Default.HelpEnvelope);
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
    }

    private static void Validate(HelpCatalogue value)
    {
        if (string.IsNullOrWhiteSpace(value.Product) || string.IsNullOrWhiteSpace(value.Invocation) || string.IsNullOrWhiteSpace(value.Description)
            || value.Commands.Length is 0 or > 100 || value.Groups.Distinct(StringComparer.Ordinal).Count() != value.Groups.Length
            || value.Commands.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Commands.Length
            || value.Commands.Any(entry => !value.Groups.Contains(entry.Group, StringComparer.Ordinal) || entry.Name.Length == 0
                || entry.Summary.Length == 0 || entry.Description.Length == 0 || entry.Usage.Length == 0
                || entry.Items.Any(item => item.Label.Length == 0 || item.Description.Length == 0)))
            throw new InvalidOperationException("Embedded command catalogue violates its typed contract.");
    }
}
