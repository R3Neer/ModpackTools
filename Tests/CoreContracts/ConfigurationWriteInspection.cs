using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;

internal static class ConfigurationWriteInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-configuration-write <request.json>.");
            using var file = File.OpenRead(args[1]); if (file.Length > 4 * 1024 * 1024) throw new ArgumentException("Request exceeds4MiB.");
            using var document = JsonDocument.Parse(file); var input = document.RootElement;
            var setting = input.GetProperty("Setting").GetString() switch { "root" => ConfigurationSetting.Root, "packwiz" => ConfigurationSetting.Packwiz, _ => throw new ArgumentException("Unknown setting.") };
            var result = ConfigurationStore.SetAsync(input.GetProperty("ConfigurationDirectory").GetString()!, new(setting, input.GetProperty("Value").GetString()!), prepareOutput: Pause).GetAwaiter().GetResult();
            async ValueTask Pause(ConfigurationWritePreview preview, CancellationToken token)
            {
                if (!input.TryGetProperty("PauseMarkerPath", out var marker)) return;
                var release = input.GetProperty("ReleasePath").GetString()!;
                File.WriteAllText(marker.GetString()!, preview.Path);
                var until = System.Diagnostics.Stopwatch.StartNew();
                while (!File.Exists(release))
                {
                    if (until.Elapsed > TimeSpan.FromSeconds(30)) throw new DomainException("Fixture.PauseTimeout", "Configuration fixture was not released", "release the owned test marker", "OperationStopped", release);
                    await Task.Delay(25, token).ConfigureAwait(false);
                }
            }
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            { writer.WriteStartObject(); writer.WriteString("Value", result.Value); writer.WriteString("Path", result.Path); writer.WriteString("Hash", result.Hash); writer.WriteEndObject(); }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check configuration fixture input", null); return 2; }
    }
}
