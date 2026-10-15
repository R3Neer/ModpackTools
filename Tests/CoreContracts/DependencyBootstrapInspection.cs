using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Dependencies;
using ModpackTools.Core.Transactions;

internal static class DependencyBootstrapInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-dependency-bootstrap <request.json>.");
            using var file = File.OpenRead(args[1]); if (file.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(file, new() { MaxDepth = 16 }); var input = document.RootElement;
            var package = input.TryGetProperty("ArchiveSha256", out var hash) ? new DependencyPackage(input.GetProperty("Version").GetString()!, hash.GetString()!, input.GetProperty("ExecutableSha256").GetString()!) : DependencyPackage.Packwiz;
            var source = new DependencyBootstrapSource(package, input.TryGetProperty("Uri", out var uri) ? new Uri(uri.GetString()!) : DependencyPackage.PackwizUri);
            using HttpMessageHandler? handler = input.TryGetProperty("ControlledHttp", out var controlled) && controlled.GetBoolean() ? new DependencyDownloadInspection.FixtureHandler(input) : null;
            using var cancellation = new CancellationTokenSource(); if (input.TryGetProperty("Cancelled", out var cancelled) && cancelled.GetBoolean()) cancellation.Cancel();
            var temporaryParent = input.GetProperty("TemporaryParent").GetString()!;
            var configuration = input.GetProperty("ConfigurationDirectory").GetString()!; var state = input.GetProperty("StateDirectory").GetString()!;
            var archivePath = input.TryGetProperty("ArchivePath", out var archive) ? archive.GetString() : null;
            var result = DependencyBootstrap.InstallAsync(source, configuration, state, temporaryParent, Environment.ProcessPath!, archivePath, cancellation.Token, handler, Observe).GetAwaiter().GetResult();
            bool? repeated = null;
            if (input.TryGetProperty("Repeat", out var repeat) && repeat.GetBoolean())
            {
                using HttpMessageHandler? repeatHandler = controlled.ValueKind == JsonValueKind.True ? new DependencyDownloadInspection.FixtureHandler(input) : null;
                repeated = DependencyBootstrap.InstallAsync(source, configuration, state, temporaryParent, Environment.ProcessPath!, archivePath, cancellation.Token, repeatHandler).GetAwaiter().GetResult().Installed;
            }
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput()); writer.WriteStartObject(); writer.WriteString("Path", result.Path); writer.WriteString("Version", result.Version); writer.WriteBoolean("Installed", result.Installed);
            if (repeated is { } value) writer.WriteBoolean("RepeatedInstalled", value);
            writer.WriteStartArray("Diagnostics"); foreach (var diagnostic in result.Diagnostics)
            { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Code); writer.WriteString("Path", diagnostic.Path); writer.WriteString("Message", diagnostic.Message); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.WriteEndObject(); return 0;
            ValueTask Observe(TransactionEvent value)
            {
                if (value.Phase == TransactionPhase.Committed && input.TryGetProperty("ContaminateAfterCommit", out var contaminate) && contaminate.GetBoolean())
                {
                    var workspace = Directory.EnumerateDirectories(temporaryParent, ".modpacktools-bootstrap-*").Single();
                    File.WriteAllText(Path.Combine(workspace, "foreign.keep"), "Preserve unexpected cleanup evidence.");
                }
                return ValueTask.CompletedTask;
            }
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check dependency bootstrap input", null); return 2; }
    }
}
