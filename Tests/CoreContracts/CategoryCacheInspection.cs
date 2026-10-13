using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class CategoryCacheInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            var resolve = arguments[0] == "--inspect-category-id";
            var required = resolve ? 5 : 2;
            if (arguments.Length < required || (arguments.Length - required) % 2 != 0)
                throw new ArgumentException("Use --inspect-read-category-cache <configuration-directory> or --inspect-category-id <project-root> <project-id> <configuration-directory> <selector> [--option value].");
            var limits = new ReferenceCacheReadLimits();
            var now = DateTimeOffset.UtcNow;
            var allow = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = required; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate category inspection option.");
                var value = arguments[index + 1];
                switch (arguments[index])
                {
                    case "--now-utc" when resolve: now = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                    case "--allow-unclassified" when resolve: allow = bool.Parse(value); break;
                    case "--max-cache-bytes": limits = limits with { MaximumBytes = long.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-depth": limits = limits with { MaximumDepth = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-rows": limits = limits with { MaximumRows = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-normalized-chars": limits = limits with { MaximumNormalizedCharacters = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-token-chars": limits = limits with { MaximumTokenCharacters = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-cache-nodes": limits = limits with { MaximumNodes = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    default: throw new ArgumentException("Unsupported category inspection option: " + arguments[index]);
                }
            }
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes, new() { MaxDepth = 1024 }))
            {
                if (resolve)
                    writer.WriteStringValue(CategorySelector.ResolveFromProject(arguments[1], arguments[2], arguments[3], arguments[4], now, allow, limits));
                else
                {
                    var cache = CategoryCacheReader.Read(arguments[1], limits);
                    if (cache is null) writer.WriteNullValue(); else cache.WriteData(writer);
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
                "check category-cache inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
