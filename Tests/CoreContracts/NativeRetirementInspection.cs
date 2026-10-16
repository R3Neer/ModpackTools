using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Distribution;

internal static class NativeRetirementInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-native-retirement <request.json>");
            using var input = File.OpenRead(args[1]);
            if (input.Length > 65536) throw new ArgumentException("Retirement request exceeds its quota");
            using var document = JsonDocument.Parse(input); var request = document.RootElement;
            using var cancellation = new CancellationTokenSource(); var changed = false;
            void Progress(NativePackageProgress value)
            {
                if (changed || value.Phase != "retire" || !request.TryGetProperty("CancelAfterFirstFile", out var cancel) || !cancel.GetBoolean()) return;
                changed = true;
                if (request.TryGetProperty("AddForeignFile", out var foreign) && foreign.GetBoolean())
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(value.Path)!, "foreign-retirement.txt"), "preserve foreign input");
                cancellation.Cancel();
            }
            var result = NativeRetirement.CollectAsync(request.GetProperty("Root").GetString()!, request.GetProperty("StateDirectory").GetString()!, cancellation.Token, Progress).GetAwaiter().GetResult();
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput()); writer.WriteStartObject(); writer.WriteBoolean("ok", true); writer.WriteStartArray("items");
            foreach (var item in result.Items)
            { writer.WriteStartObject(); writer.WriteString("build_id", item.BuildId); writer.WriteString("status", item.Status); writer.WriteString("path", item.Path); writer.WriteString("reason", item.Reason); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.WriteEndObject(); return 0;
        }
        catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or ArgumentException or JsonException or OperationCanceledException)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput()); writer.WriteStartObject(); writer.WriteBoolean("ok", false);
            writer.WriteBoolean("cancelled", error is OperationCanceledException); writer.WriteString("message", error.Message); writer.WriteEndObject(); return 1;
        }
    }
}
