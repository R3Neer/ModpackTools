using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ModpackTools.Core.Processes;

// Private entry point of the same Native AOT host. It cannot spawn before stdin
// contains the entire payload, sent only after the parent established ownership.
public static class NativeToolChild
{
    public static async Task<int> RunAsync()
    {
        NativeToolResult result;
        try
        {
            var bytes = await NativeTool.ReadBoundedAsync(Console.OpenStandardInput(), 1024 * 1024, CancellationToken.None).ConfigureAwait(false);
            using var input = JsonDocument.Parse(bytes, new() { MaxDepth = 8 }); var root = input.RootElement;
            var executable = root.GetProperty("Executable").GetString()!; var directory = root.GetProperty("Directory").GetString()!;
            var arguments = root.GetProperty("Arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
            var maximum = root.GetProperty("MaximumOutputBytes").GetInt32();
            NativeTool.Validate(executable, arguments, directory, new(MaximumOutputBytes: maximum));
            result = await Execute(executable, arguments, directory, maximum).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { result = new(null, "", "", error is DomainException domain ? domain.Id : "ModpackTools.Build.InvalidHelperRequest",
            error is DomainException detail ? detail.Message + "; Try: " + detail.Hint
                + (detail.Target is TextTarget target ? "; Target: " + target.Value : "")
                + (detail.Details is null ? "" : "; Details: " + detail.Details) : error.Message); }
        using var output = new Utf8JsonWriter(Console.OpenStandardOutput());
        output.WriteStartObject();
        if (result.ExitCode is { } code) output.WriteNumber("ExitCode", code); else output.WriteNull("ExitCode");
        output.WriteString("Stdout", result.Stdout); output.WriteString("Stderr", result.Stderr);
        output.WriteString("FailureId", result.FailureId); output.WriteString("FailureMessage", result.FailureMessage);
        output.WriteEndObject(); output.Flush(); return 0;
    }

    private static async Task<NativeToolResult> Execute(string executable, string[] arguments, string directory, int maximum)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new WindowsProcess { StartInfo = start };
        try { if (!process.Start()) return new(null, "", "", "ModpackTools.Build.ProcessStartFailed", "Tool did not start"); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { return new(null, "", "", "ModpackTools.Build.ProcessStartFailed", error.Message); }
        process.StandardInput.Close();
        using var stdout = new MemoryStream(); using var stderr = new MemoryStream();
        using var stop = new CancellationTokenSource(); long delivered = 0; var exceeded = 0;
        var first = Capture(process.StandardOutput.BaseStream, stdout); var second = Capture(process.StandardError.BaseStream, stderr);
        try { await Task.WhenAll(first, second, process.WaitForExitAsync(stop.Token)).ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or IOException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            try { await Task.WhenAll(first, second).ConfigureAwait(false); } catch (Exception drain) when (drain is OperationCanceledException or IOException) { }
            return Result(exceeded != 0 ? "ModpackTools.Build.ProcessOutputLimit" : "ModpackTools.Build.ProcessIoFailed", error.Message);
        }
        return Result(null, null);

        NativeToolResult Result(string? id, string? message) => new(process.ExitCode, Encoding.UTF8.GetString(stdout.GetBuffer(), 0, checked((int)stdout.Length)),
            Encoding.UTF8.GetString(stderr.GetBuffer(), 0, checked((int)stderr.Length)), id, message);
        async Task Capture(Stream source, MemoryStream destination)
        {
            var buffer = new byte[8192]; int read;
            try
            {
                while ((read = await source.ReadAsync(buffer, stop.Token).ConfigureAwait(false)) != 0)
                {
                    if (Interlocked.Add(ref delivered, read) > maximum)
                    {
                        Interlocked.Exchange(ref exceeded, 1); stop.Cancel();
                        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                        throw new OperationCanceledException("Combined tool output exceeds its byte quota");
                    }
                    destination.Write(buffer, 0, read);
                }
            }
            catch { stop.Cancel(); throw; }
        }
    }
}
