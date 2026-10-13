using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class ResourceOrderInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 2 && (arguments.Length != 4 || arguments[2] != "--max-items"))
                throw new ArgumentException("Use --inspect-resource-order <root> [--max-items n].");
            var limits = arguments.Length == 4 ? new ResourceOrderReadLimits(int.Parse(arguments[3], CultureInfo.InvariantCulture)) : new();
            var ids = ResourcePackOrderReader.Read(ProjectReader.Read(arguments[1]), limits);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartArray();
            foreach (var id in ids) writer.WriteStringValue(id);
            writer.WriteEndArray();
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
                "check resource-order inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
