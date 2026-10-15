using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Dependencies;

internal static class DependencyArchiveInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-dependency-archive <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 }); var input = document.RootElement;
            var package = input.TryGetProperty("ArchiveSha256", out var hash) ? new DependencyPackage(input.GetProperty("Version").GetString()!, hash.GetString()!, input.GetProperty("ExecutableSha256").GetString()!) : DependencyPackage.Packwiz;
            using var cancellation = new CancellationTokenSource(); if (input.TryGetProperty("Cancelled", out var cancelled) && cancelled.GetBoolean()) cancellation.Cancel();
            var result = DependencyArchive.Prepare(input.GetProperty("ArchivePath").GetString()!, input.GetProperty("StagingParent").GetString()!, package, cancellation: cancellation.Token);
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteString("Directory", result.Directory); writer.WriteString("Version", result.Version); writer.WriteStartArray("Files");
                foreach (var file in result.Files) { writer.WriteStartObject(); writer.WriteString("Name", file.Name); writer.WriteString("Sha256", file.Sha256); writer.WriteNumber("Length", file.Length); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check dependency preparation input", null); return 2; }
    }
}
