using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class CacheReadInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            var resolve = arguments[0] == "--inspect-resolve-reference";
            var required = resolve ? 4 : 2;
            if (arguments.Length < required || (arguments.Length - required) % 2 != 0)
                throw new ArgumentException("Use --inspect-read-reference-cache <configuration-directory> or --inspect-resolve-reference <configuration-directory> <selector> <project-id> [--option value].");
            var limits = new ReferenceCacheReadLimits();
            var now = DateTimeOffset.UtcNow;
            string[]? kinds = ["mod", "resourcepack", "shaderpack"];
            var requirePackwiz = false;
            string? stringField = null;
            for (var index = required; index < arguments.Length; index += 2)
            {
                var value = arguments[index + 1];
                switch (arguments[index])
                {
                    case "--max-cache-bytes": limits = limits with { MaximumBytes = long.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-depth": limits = limits with { MaximumDepth = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-rows": limits = limits with { MaximumRows = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-normalized-chars": limits = limits with { MaximumNormalizedCharacters = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-token-chars": limits = limits with { MaximumTokenCharacters = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-nodes": limits = limits with { MaximumNodes = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--string-field" when resolve: stringField = value; break;
                    case "--now-utc" when resolve: now = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                    case "--require-packwiz" when resolve: requirePackwiz = bool.Parse(value); break;
                    case "--allowed-kinds" when resolve:
                        using (var parsed = JsonDocument.Parse(value))
                            kinds = parsed.RootElement.ValueKind == JsonValueKind.Null ? null
                                : parsed.RootElement.EnumerateArray().Select(item => item.GetString() ?? throw new FormatException("Allowed kinds must be strings.")).ToArray();
                        break;
                    default: throw new ArgumentException("Unsupported cache inspection option: " + arguments[index]);
                }
            }
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes, new() { MaxDepth = 1024 }))
            {
                if (resolve && stringField == "Selector")
                    writer.WriteStringValue(ReferenceCacheResolver.ResolveSelectorFromDirectory(arguments[1], arguments[2], arguments[3], now,
                        new ReferenceLookupPolicy(kinds, requirePackwiz), limits));
                else if (resolve)
                {
                    var row = ReferenceCacheResolver.ResolveFromDirectory(arguments[1], arguments[2], arguments[3], now,
                        new ReferenceLookupPolicy(kinds, requirePackwiz), limits);
                    if (row is null) writer.WriteNullValue();
                    else if (stringField is not null) writer.WriteStringValue(row.StringField(stringField));
                    else row.WriteData(writer);
                }
                else
                {
                    var snapshot = ReferenceCacheReader.Read(arguments[1], limits);
                    if (snapshot is null) writer.WriteNullValue(); else snapshot.WriteData(writer);
                }
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
                "check reference-cache inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
