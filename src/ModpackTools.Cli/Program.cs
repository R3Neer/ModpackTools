using System.Text;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Cli;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;

Console.OutputEncoding = new UTF8Encoding(false);
try
{
    if (args.Length > 0 && args[0] == "--request-path")
    {
        if (args.Length != 2)
            throw new ProtocolException("Use --request-path <file> without additional arguments.");
        args = RequestReader.Read(args[1]);
    }
}
catch (Exception exception) when (exception is ProtocolException or JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    var invalid = FailureEnvelope.Create("", [],
        Failure.From("Core.InvalidRequest", exception.Message, "InvalidData", null));
    Console.WriteLine(JsonSerializer.Serialize(invalid, WireJsonContext.Default.FailureEnvelope));
    return 2;
}

var command = "";
string[] commandArguments = [];
try
{
    var options = PresentationOptions.Parse(args);
    command = options.Arguments.FirstOrDefault() ?? "";
    commandArguments = options.Arguments.Skip(1).ToArray();
    if (command.Equals("--version", StringComparison.OrdinalIgnoreCase))
    {
        var version = ProductVersion.Read(commandArguments, options.Project);
        var envelope = new VersionEnvelope(1, true, command, commandArguments, new(version));
        Console.WriteLine(JsonSerializer.Serialize(envelope, WireJsonContext.Default.VersionEnvelope));
        return 0;
    }
    if (command.Equals("project", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("list", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        if (options.Project is not null)
            throw new DomainException("Option.ForbiddenCombination", "Option '--project' is not valid for this operation", "modpack project list", target: options.Project);
        if (commandArguments.Length != 1)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack project list", targetData: commandArguments.Length == 2 ? new TextTarget(commandArguments[1]) : new TextListTarget(commandArguments.Skip(1).ToArray()));
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        var catalog = ProjectReader.Discover(ConfigurationReader.ReadRoot(Path.GetFullPath(configurationDirectory)));
        var envelope = new ProjectListEnvelope(1, true, command, commandArguments, new(new(catalog.Root, catalog.Projects.Select(PublicProject.From).ToArray())));
        Console.WriteLine(JsonSerializer.Serialize(envelope, WireJsonContext.Default.ProjectListEnvelope));
        return 0;
    }
}
catch (DomainException exception)
{
    var message = Sentence(exception.Message);
    if (!string.IsNullOrWhiteSpace(exception.Details)) message += Environment.NewLine + "Details: " + Sentence(exception.Details);
    message += Environment.NewLine + "Try: " + exception.Hint;
    var domainError = FailureEnvelope.Create(command, commandArguments,
        Failure.From(exception.Id, message, exception.Category, exception.Target));
    Console.WriteLine(JsonSerializer.Serialize(domainError, WireJsonContext.Default.FailureEnvelope));
    return 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
{
    var inputError = FailureEnvelope.Create(command, commandArguments,
        Failure.From("Core.InputReadFailed", "Could not read project data. " + exception.Message + Environment.NewLine + "Try: check the file paths, data types and access permissions", "InvalidData", null));
    Console.WriteLine(JsonSerializer.Serialize(inputError, WireJsonContext.Default.FailureEnvelope));
    return 1;
}

var failure = FailureEnvelope.Create(command, commandArguments,
    Failure.From("Core.CapabilityUnavailable", "This native host currently supports version and project list.", "InvalidOperation", null));
Console.WriteLine(JsonSerializer.Serialize(failure, WireJsonContext.Default.FailureEnvelope));
return 1;

static string Sentence(string text)
{
    var result = text.Trim();
    return result.EndsWith('.') || result.EndsWith('!') || result.EndsWith('?') ? result : result + ".";
}
