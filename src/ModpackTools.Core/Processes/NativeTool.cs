using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ModpackTools.Core.Processes;

public sealed record NativeToolLimits(int TimeoutMilliseconds = 120000, int MaximumOutputBytes = 1024 * 1024, int CleanupMilliseconds = 10000);
public sealed record NativeToolResult(int? ExitCode, string Stdout, string Stderr, string? FailureId = null, string? FailureMessage = null);

public static class NativeTool
{
    public static async Task<NativeToolResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory,
        string nativeHost, NativeToolLimits? limits = null, CancellationToken cancellation = default,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        limits ??= new(); Validate(executable, arguments, directory, limits);
        if (!Path.IsPathFullyQualified(nativeHost) || !File.Exists(nativeHost) || !nativeHost.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw Error("InvalidHelper", "Native helper host must be an existing absolute executable", nativeHost);
        if (cancellation.IsCancellationRequested) throw new DomainException("Core.OperationCancelled", "Native tool execution was cancelled", "retry when ready", "OperationStopped");
        var payload = Payload(executable, arguments, directory, limits.MaximumOutputBytes);
        using var job = new WindowsToolJob();
        using var process = new Process
        {
            StartInfo = new(nativeHost)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("--tool-child");
        if (environment is not null)
            foreach (var pair in environment)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Key.Contains('=') || pair.Key.Contains('\0') || pair.Value?.Contains('\0') is true)
                    throw Error("InvalidEnvironment", "Native child environment contains an invalid entry", pair.Key);
                if (pair.Value is null) process.StartInfo.Environment.Remove(pair.Key);
                else process.StartInfo.Environment[pair.Key] = pair.Value;
            }
        var started = false; var assigned = false; DomainException? failure = null; DomainException? drainFailure = null; NativeToolResult? result = null;
        using var deadline = new CancellationTokenSource(limits.TimeoutMilliseconds);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
        Task<byte[]>? stdout = null; Task<byte[]>? stderr = null;
        try
        {
            try { started = process.Start(); }
            catch (System.ComponentModel.Win32Exception error) { throw Error("ProcessStartFailed", "Native helper could not start", nativeHost, error.Message); }
            if (!started) throw Error("ProcessStartFailed", "Native helper did not start", nativeHost);
            job.Assign(process); assigned = true;
            stdout = Capture(process.StandardOutput.BaseStream, checked(limits.MaximumOutputBytes * 6 + 65536));
            stderr = Capture(process.StandardError.BaseStream, 65536);
            await process.StandardInput.BaseStream.WriteAsync(payload, stop.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            // A failed bounded drain cancels the other waits immediately.
            var exit = process.WaitForExitAsync(stop.Token);
            var complete = Task.WhenAll(stdout, stderr, exit);
            await complete.WaitAsync(stop.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 || stderr.Result.Length != 0) throw Error("InvalidHelperResult", "Native helper failed outside its private result protocol", nativeHost,
                Encoding.UTF8.GetString(stderr.Result));
            result = ReadResult(stdout.Result);
            if (result.FailureId is { } id) throw Error(id["ModpackTools.Build.".Length..], "Native tool execution failed", executable, result.FailureMessage,
                id.EndsWith("OutputLimit", StringComparison.Ordinal) ? "LimitsExceeded" : "OperationStopped");
            if (result.ExitCode != 0) throw Error("ProcessFailed", $"Native tool exited with code {result.ExitCode}", executable,
                result.Stdout + result.Stderr, "OperationStopped");
        }
        catch (OperationCanceledException)
        {
            failure = drainFailure ?? (cancellation.IsCancellationRequested
            ? new DomainException("Core.OperationCancelled", "Native tool execution was cancelled", "retry when ready", "OperationStopped", executable)
            : Error("ProcessTimeout", "Native tool deadline expired", executable, category: "OperationStopped"));
        }
        catch (DomainException error) { failure = error; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        { failure = Error("ProcessIoFailed", "Native tool protocol IO failed", executable, error.Message); }
        finally
        {
            if (started)
            {
                stop.Cancel();
                try
                {
                    if (assigned) job.Terminate(); else if (!process.HasExited) process.Kill(entireProcessTree: true);
                    using var cleanup = new CancellationTokenSource(limits.CleanupMilliseconds);
                    await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                    while (assigned && !job.Empty) await Task.Delay(10, cleanup.Token).ConfigureAwait(false);
                    if (stdout is not null && stderr is not null)
                    { try { await Task.WhenAll(stdout, stderr).WaitAsync(cleanup.Token).ConfigureAwait(false); } catch (Exception drain) when (drain is OperationCanceledException or IOException or DomainException) { } }
                }
                catch (Exception error) when (error is DomainException or OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    var retained = Error("ProcessOwnershipUncertain", "Tool termination could not be confirmed; retain its stage", directory,
                        "Primary: " + (failure?.Message ?? "completed") + "; Cleanup: " + error.Message, "OperationStopped");
                    retained.Data["ModpackTools.RetainStage"] = true; failure = retained;
                }
            }
        }
        if (failure is not null) throw failure;
        return result!;

        async Task<byte[]> Capture(Stream source, int maximum)
        {
            try { return await ReadBoundedAsync(source, maximum, stop.Token).ConfigureAwait(false); }
            catch (DomainException error) { Interlocked.CompareExchange(ref drainFailure, error, null); stop.Cancel(); throw; }
            catch (IOException error)
            {
                var failure = Error("ProcessIoFailed", "Native helper output could not be read", nativeHost, error.Message);
                Interlocked.CompareExchange(ref drainFailure, failure, null); stop.Cancel(); throw failure;
            }
        }
    }

    internal static void Validate(string executable, IReadOnlyList<string> arguments, string directory, NativeToolLimits limits)
    {
        if (limits.TimeoutMilliseconds is <= 0 or > 3600000 || limits.MaximumOutputBytes is <= 0 or > 8 * 1024 * 1024 || limits.CleanupMilliseconds is <= 0 or > 60000)
            throw Error("InvalidProcessPolicy", "Native tool limits are invalid");
        if (!Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
            throw Error("CommandNotFound", "Native executable is not available", executable, category: "ObjectNotFound");
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) throw Error("InvalidWorkingDirectory", "Tool working directory is unavailable", directory);
        if (arguments.Count > 1024 || arguments.Any(value => value is null || value.IndexOf('\0') >= 0) || arguments.Sum(value => (long)value.Length) > 256 * 1024)
            throw Error("InvalidArguments", "Tool arguments exceed their supported quota");
    }
    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellation)
    {
        using var result = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        { if (read > maximum - result.Length) throw Error("ProcessOutputLimit", "Native helper transport exceeds its byte quota", category: "LimitsExceeded"); result.Write(buffer, 0, read); }
        return result.ToArray();
    }
    private static byte[] Payload(string executable, IReadOnlyList<string> arguments, string directory, int maximum)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject(); writer.WriteString("Executable", executable); writer.WriteString("Directory", directory); writer.WriteNumber("MaximumOutputBytes", maximum);
            writer.WriteStartArray("Arguments"); foreach (var argument in arguments) writer.WriteStringValue(argument); writer.WriteEndArray(); writer.WriteEndObject();
        }
        if (bytes.Length > 1024 * 1024) throw Error("InvalidArguments", "Tool payload exceeds1MiB");
        return bytes.ToArray();
    }
    private static NativeToolResult ReadResult(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 }); var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Error("InvalidHelperResult", "Native helper result must be an object");
        var fields = new HashSet<string>(["ExitCode", "Stdout", "Stderr", "FailureId", "FailureMessage"], StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject()) if (!fields.Remove(property.Name)) throw Error("InvalidHelperResult", "Native helper result has duplicate or unknown fields");
        if (fields.Count != 0 || root.GetProperty("Stdout").ValueKind != JsonValueKind.String || root.GetProperty("Stderr").ValueKind != JsonValueKind.String)
            throw Error("InvalidHelperResult", "Native helper result has missing or invalid fields");
        var result = new NativeToolResult(root.GetProperty("ExitCode").ValueKind == JsonValueKind.Null ? null : root.GetProperty("ExitCode").GetInt32(),
            root.GetProperty("Stdout").GetString()!, root.GetProperty("Stderr").GetString()!,
            root.GetProperty("FailureId").GetString(), root.GetProperty("FailureMessage").GetString());
        if (result.FailureId is { } id && (!id.StartsWith("ModpackTools.Build.", StringComparison.Ordinal) || id.Length <= "ModpackTools.Build.".Length)
            || result.ExitCode is null && result.FailureId is null) throw Error("InvalidHelperResult", "Native helper result has an invalid completion state");
        return result;
    }
    private static DomainException Error(string id, string message, string? target = null, string? details = null, string category = "ResourceUnavailable") =>
        new("Build." + id, message, "inspect the native tool and its bounded output before retrying", category, target, details: details);
}
