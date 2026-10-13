using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class CategoryInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 2) throw new ArgumentException("Use --inspect-categories <root>.");
            var definitions = CategoryDefinitions.Read(EditorialMetadataReader.Read(ProjectReader.Read(arguments[1])));
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartArray();
            foreach (var definition in definitions)
            {
                writer.WriteStartObject();
                writer.WriteString("key", definition.Key);
                writer.WriteString("name", definition.Name);
                writer.WriteNumber("order", definition.Order);
                writer.WriteEndObject();
            }
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
                "check category inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
