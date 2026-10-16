using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class CacheWriteInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 3 || (arguments.Length - 3) % 2 != 0)
                throw new ArgumentException("Use --inspect-write-reference-cache <root> <configuration-directory> [--option value].");
            var queryArguments = new List<string> { "--inspect-numbered-inventory", arguments[1] };
            var timestamp = DateTimeOffset.UtcNow;
            var limits = new ReferenceCacheWriteLimits();
            for (var index = 3; index < arguments.Length; index += 2)
            {
                if (arguments[index] == "--created-utc") timestamp = DateTimeOffset.Parse(arguments[index + 1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                else if (arguments[index] == "--max-cache-bytes") limits = limits with { MaximumBytes = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture) };
                else { queryArguments.Add(arguments[index]); queryArguments.Add(arguments[index + 1]); }
            }
            var query = SelectionInspection.ParseQuery(queryArguments.ToArray());
            var inventory = InventoryReferences.Assign(InventorySelector.Select(InventoryReader.Read(ProjectReader.Read(arguments[1])), query));
            var result = InventoryReferenceCache.Write(arguments[2], inventory, timestamp, limits);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartObject();
            writer.WriteString("path", result.Path);
            writer.WriteNumber("count", result.Count);
            writer.WriteEndObject();
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null,
                "check reference-cache inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
