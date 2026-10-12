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
    var invalid = new FailureEnvelope(1, false, "", [],
        new("Core.InvalidRequest", exception.Message, "InvalidData", null));
    Console.WriteLine(JsonSerializer.Serialize(invalid, WireJsonContext.Default.FailureEnvelope));
    return 2;
}

if (args is ["--version"])
{
    var envelope = new VersionEnvelope(1, true, "--version", [], new(ProductVersion.Current));
    Console.WriteLine(JsonSerializer.Serialize(envelope, WireJsonContext.Default.VersionEnvelope));
    return 0;
}

var failure = new FailureEnvelope(1, false, args.FirstOrDefault() ?? "", args.Skip(1).ToArray(),
    new("Core.CapabilityUnavailable", "This native host currently supports --version only.", "InvalidOperation", null));
Console.WriteLine(JsonSerializer.Serialize(failure, WireJsonContext.Default.FailureEnvelope));
return 1;
