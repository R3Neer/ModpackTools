using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Processes;

internal static class PackwizRefreshInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-packwiz-refresh <request.json>.");
            using var file = File.OpenRead(args[1]); if (file.Length > 1024 * 1024) throw new ArgumentException("Process fixture exceeds1MiB.");
            using var document = JsonDocument.Parse(file, new() { MaxDepth = 16 }); var input = document.RootElement;
            var tool = input.TryGetProperty("Executable", out var executable) ? new PackwizObservation(true, executable.GetString(), "explicit", null)
                : PackwizRefresh.Resolve(Text(input, "ConfiguredPath"), Text(input, "PathValue"), Text(input, "ManagedPath"));
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                if (input.TryGetProperty("ResolveOnly", out var resolve) && resolve.GetBoolean())
                { writer.WriteStartObject(); WriteTool(writer, tool); writer.WriteEndObject(); }
                else
                {
                    var root = Path.GetFullPath(Text(input, "Root")!); var limits = new NativeToolLimits();
                    if (input.TryGetProperty("TimeoutMilliseconds", out var timeout)) limits = limits with { TimeoutMilliseconds = timeout.GetInt32() };
                    if (input.TryGetProperty("MaximumOutputBytes", out var maximum)) limits = limits with { MaximumOutputBytes = maximum.GetInt32() };
                    using var cancellation = new CancellationTokenSource();
                    if (input.TryGetProperty("CancellationMilliseconds", out var cancel)) { if (cancel.GetInt32() == 0) cancellation.Cancel(); else cancellation.CancelAfter(cancel.GetInt32()); }
                    var host = Text(input, "NativeHost") ?? Environment.ProcessPath!;
                    writer.WriteStartObject();
                    if (input.TryGetProperty("Arguments", out var arguments))
                    {
                        var result = NativeTool.RunAsync(tool.Path!, arguments.EnumerateArray().Select(value => value.GetString()!).ToArray(), root, host, limits, cancellation.Token).GetAwaiter().GetResult();
                        WriteOutput(writer, result);
                    }
                    else
                    {
                        var result = PackwizRefresh.RefreshAsync(ContentMaterializationInspection.Context(root), tool, host, limits, cancellation.Token).GetAwaiter().GetResult();
                        WriteTool(writer, result.Tool); writer.WriteString("IndexFile", result.IndexFile); writer.WriteString("HashAlgorithm", result.HashAlgorithm);
                        writer.WriteString("IndexHash", result.IndexHash); WriteOutput(writer, result.Output);
                    }
                    writer.WriteEndObject();
                }
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check process fixture", null); return 2; }
    }
    private static void WriteTool(Utf8JsonWriter writer, PackwizObservation tool)
    { writer.WriteBoolean("Available", tool.Available); writer.WriteString("Path", tool.Path); writer.WriteString("Source", tool.Source); writer.WriteString("Version", tool.Version); }
    private static void WriteOutput(Utf8JsonWriter writer, NativeToolResult result)
    { writer.WriteNumber("ExitCode", result.ExitCode!.Value); writer.WriteString("Stdout", result.Stdout); writer.WriteString("Stderr", result.Stderr); }
    private static string? Text(JsonElement input, string name) => input.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
}
