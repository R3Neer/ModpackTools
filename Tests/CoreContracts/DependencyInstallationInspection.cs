using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Dependencies;
using ModpackTools.Core.Transactions;

internal static class DependencyInstallationInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-dependency-installation <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 }); var input = document.RootElement;
            var package = input.TryGetProperty("ArchiveSha256", out var hash) ? new DependencyPackage(input.GetProperty("Version").GetString()!, hash.GetString()!, input.GetProperty("ExecutableSha256").GetString()!) : DependencyPackage.Packwiz;
            using var cancellation = new CancellationTokenSource(); if (input.TryGetProperty("Cancelled", out var cancelled) && cancelled.GetBoolean()) cancellation.Cancel();
            var prepared = DependencyArchive.Prepare(input.GetProperty("ArchivePath").GetString()!, input.GetProperty("StagingParent").GetString()!, package, cancellation: cancellation.Token);
            var result = DependencyInstaller.InstallAsync(prepared, input.GetProperty("TargetDirectory").GetString()!, input.GetProperty("StateDirectory").GetString()!, package, Environment.ProcessPath!, cancellation.Token, Observe).GetAwaiter().GetResult();
            bool? repeated = null;
            if (input.TryGetProperty("Repeat", out var repeat) && repeat.GetBoolean())
                repeated = DependencyInstaller.InstallAsync(prepared, input.GetProperty("TargetDirectory").GetString()!, input.GetProperty("StateDirectory").GetString()!, package, Environment.ProcessPath!, cancellation.Token).GetAwaiter().GetResult().Installed;
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteString("Path", result.Path); writer.WriteString("Version", result.Version); writer.WriteBoolean("Installed", result.Installed);
                writer.WriteString("PreparedDirectory", prepared.Directory); if (repeated is { } value) writer.WriteBoolean("RepeatedInstalled", value);
                writer.WriteStartArray("Diagnostics"); foreach (var diagnostic in result.Diagnostics)
                { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Code); writer.WriteString("Path", diagnostic.Path); writer.WriteString("Message", diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
            ValueTask Observe(TransactionEvent value)
            {
                if (input.TryGetProperty("FailAfterWrites", out var count) && value.Phase == TransactionPhase.FileCommitted && value.CompletedFiles >= count.GetInt32())
                    throw new IOException("fixture dependency publication failure");
                return ValueTask.CompletedTask;
            }
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check dependency installation input", null); return 2; }
    }
}
