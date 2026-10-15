using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;

internal static class CategoryCreationInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-category-creation <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 4 * 1024 * 1024) throw new ArgumentException("Request exceeds4MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 32 }); var input = document.RootElement;
            var request = new CategoryCreationRequest(input.GetProperty("Id").GetString()!,
                input.TryGetProperty("Name", out var name) ? name.GetString() : null,
                input.TryGetProperty("Order", out var order) && order.ValueKind != JsonValueKind.Null ? order.GetInt32() : null);
            var now = input.TryGetProperty("Now", out var date) ? DateTimeOffset.Parse(date.GetString()!, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow;
            var result = CategoryCreation.ExecuteAsync(ProjectReader.Read(input.GetProperty("Root").GetString()!), request,
                input.GetProperty("ConfigurationDirectory").GetString()!, input.GetProperty("StateDirectory").GetString()!, now).GetAwaiter().GetResult();
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteBoolean("Applied", result.Applied); writer.WritePropertyName("View");
                CategoryListInspection.Write(writer, result.Result);
                writer.WriteStartArray("Changes"); foreach (var change in result.Changes)
                { writer.WriteStartObject(); writer.WriteString("Path", change.Path); writer.WriteString("Before", change.Before); writer.WriteString("After", change.After); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteStartArray("Diagnostics");
                foreach (var diagnostic in result.Diagnostics)
                { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Code); writer.WriteString("Path", diagnostic.Path); writer.WriteString("Message", diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check category creation fixture input", null); return 2; }
    }
}
