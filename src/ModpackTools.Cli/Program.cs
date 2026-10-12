using System.Text;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Cli;

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
        new("Core.InvalidRequest", exception.Message, "InvalidData", null));
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
}
catch (DomainException exception)
{
    var message = exception.Message.Trim();
    if (!message.EndsWith('.') && !message.EndsWith('!') && !message.EndsWith('?')) message += ".";
    message += Environment.NewLine + "Try: " + exception.Hint;
    var domainError = FailureEnvelope.Create(command, commandArguments,
        new(exception.Id, message, exception.Category, exception.Target));
    Console.WriteLine(JsonSerializer.Serialize(domainError, WireJsonContext.Default.FailureEnvelope));
    return 1;
}

var failure = FailureEnvelope.Create(command, commandArguments,
    new("Core.CapabilityUnavailable", "This native host currently supports --version only.", "InvalidOperation", null));
Console.WriteLine(JsonSerializer.Serialize(failure, WireJsonContext.Default.FailureEnvelope));
return 1;
