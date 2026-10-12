using System.Text;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Cli;

Console.OutputEncoding = new UTF8Encoding(false);
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
