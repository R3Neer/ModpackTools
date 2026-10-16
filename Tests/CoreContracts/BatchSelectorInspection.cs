using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class BatchSelectorInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 4 || (arguments.Length - 4) % 2 != 0)
                throw new ArgumentException("Use --inspect-batch-selectors <configuration-directory> <project-id> <selectors-json-array> [--option value].");
            ImmutableArray<string> selectors;
            using (var parsed = JsonDocument.Parse(arguments[3], new JsonDocumentOptions { MaxDepth = 16 }))
            {
                var array = parsed.RootElement;
                if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 1024)
                    throw new ArgumentException("Supply an array of zero through 1024 selectors.");
                var values = ImmutableArray.CreateBuilder<string>(array.GetArrayLength());
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString()))
                        throw new ArgumentException("Selectors must be nonempty strings.");
                    values.Add(item.GetString()!);
                }
                selectors = values.MoveToImmutable();
            }
            var limits = new ReferenceCacheReadLimits();
            var now = DateTimeOffset.UtcNow;
            string[]? kinds = ["mod"];
            var requirePackwiz = false;
            for (var index = 4; index < arguments.Length; index += 2)
            {
                var value = arguments[index + 1];
                switch (arguments[index])
                {
                    case "--now-utc": now = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                    case "--require-packwiz": requirePackwiz = bool.Parse(value); break;
                    case "--allowed-kinds":
                        using (var parsed = JsonDocument.Parse(value))
                            kinds = parsed.RootElement.ValueKind == JsonValueKind.Null ? null
                                : parsed.RootElement.EnumerateArray().Select(item => item.GetString() ?? throw new FormatException("Allowed kinds must be strings.")).ToArray();
                        break;
                    case "--max-cache-bytes": limits = limits with { MaximumBytes = long.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-depth": limits = limits with { MaximumDepth = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-rows": limits = limits with { MaximumRows = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-normalized-chars": limits = limits with { MaximumNormalizedCharacters = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-token-chars": limits = limits with { MaximumTokenCharacters = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-nodes": limits = limits with { MaximumNodes = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    default: throw new ArgumentException("Unsupported batch inspection option: " + arguments[index]);
                }
            }
            var result = BatchSelectorResolver.ResolveFromDirectory(selectors, arguments[1], arguments[2], now,
                new ReferenceLookupPolicy(kinds, requirePackwiz), limits);
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartArray();
                foreach (var value in result) writer.WriteStringValue(value);
                writer.WriteEndArray();
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null,
                "check batch inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
