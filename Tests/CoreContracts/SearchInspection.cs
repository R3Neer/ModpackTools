using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;

internal static class SearchInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 8 || (arguments.Length - 8) % 2 != 0)
                throw new ArgumentException("Use --inspect-search <project-or-empty> <config> <query> <type> <limit> <now> <base-uri> [--option value].");
            var requests = new ProviderRequestLimits(); var cache = new ReferenceCacheWriteLimits();
            using var cancellation = new CancellationTokenSource();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 8; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate search inspection option.");
                var number = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--max-response-bytes": requests = requests with { MaximumResponseBytes = number }; break;
                    case "--max-results": requests = requests with { MaximumSearchResults = checked((int)number) }; break;
                    case "--max-cache-bytes": cache = new(number); break;
                    case "--cancel-after-ms": if (number == 0) cancellation.Cancel(); else cancellation.CancelAfter(checked((int)number)); break;
                    default: throw new ArgumentException("Unknown search inspection option.");
                }
            }
            var project = arguments[1].Length == 0 ? null : ProjectReader.Read(arguments[1]);
            var view = ProviderSearch.ReadAsync(arguments[3], arguments[4], int.Parse(arguments[5], CultureInfo.InvariantCulture), project, arguments[2],
                DateTimeOffset.Parse(arguments[6], CultureInfo.InvariantCulture), requests, cache, cancellation.Token, baseUri: new(arguments[7])).GetAwaiter().GetResult();
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartObject(); writer.WriteString("query", view.Query); writer.WriteString("type", view.Type); writer.WriteNumber("total_hits", view.Results.TotalHits);
            writer.WriteStartArray("results");
            foreach (var row in view.Results.Items)
            {
                writer.WriteStartObject(); writer.WriteNumber("number", row.Number); writer.WriteString("id", row.ProjectId); writer.WriteString("slug", row.Slug); writer.WriteString("name", row.Title);
                writer.WriteString("type", row.Type); writer.WriteString("author", row.Author); writer.WriteNumber("downloads", row.Downloads); writer.WriteString("description", row.Description); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject(); return 0;
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check search inspection arguments", null); return 2; }
    }
}
