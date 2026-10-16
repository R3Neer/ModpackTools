using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class CategoryCacheWriteInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 3 || (arguments.Length - 3) % 2 != 0)
                throw new ArgumentException("Use --inspect-write-category-cache <root> <configuration-directory> [--created-utc timestamp] [--max-cache-bytes n] [--empty-categories bool].");
            var timestamp = DateTimeOffset.UtcNow;
            var limits = new ReferenceCacheWriteLimits();
            var empty = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 3; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate category-cache writer inspection option.");
                switch (arguments[index])
                {
                    case "--created-utc": timestamp = DateTimeOffset.Parse(arguments[index + 1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                    case "--max-cache-bytes": limits = limits with { MaximumBytes = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture) }; break;
                    case "--empty-categories": empty = bool.Parse(arguments[index + 1]); break;
                    default: throw new ArgumentException("Unknown category-cache writer inspection option.");
                }
            }
            var view = CategoryList.Read(ProjectReader.Read(arguments[1]));
            // Diagnostic-only producer for the initialized empty typed-view contract.
            if (empty) view = view with { Categories = ImmutableArray<CategoryListEntry>.Empty, DefinedCategoryCount = 0 };
            var result = CategoryReferenceCache.Write(arguments[2], view, timestamp, limits);
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject();
                writer.WriteString("path", result.Path);
                writer.WriteNumber("count", result.Count);
                writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or JsonException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null,
                "check category-cache writer inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
