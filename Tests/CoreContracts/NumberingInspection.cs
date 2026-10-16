using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class NumberingInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            var query = SelectionInspection.ParseQuery(arguments);
            var view = InventoryReferences.Assign(InventorySelector.Select(InventoryReader.Read(ProjectReader.Read(arguments[1])), query));
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartObject();
            writer.WriteStartArray("categories");
            foreach (var category in view.Categories)
            {
                writer.WriteStartObject();
                writer.WriteString("key", category.Key);
                writer.WriteString("name", category.Name);
                writer.WriteNumber("order", category.Order);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            WritePhysical(writer, "mods", view.Mods);
            writer.WriteStartArray("active");
            foreach (var row in view.ActiveResources)
            {
                writer.WriteStartObject();
                writer.WriteNumber("number", row.Number);
                writer.WritePropertyName("item");
                ResourceViewInspection.WriteActive(writer, row.Item);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            WritePhysical(writer, "inactive", view.InactiveResources);
            WritePhysical(writer, "shaders", view.Shaders);
            writer.WriteStartArray("references");
            foreach (var reference in view.References)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", reference.Index);
                writer.WriteString("id", reference.Id);
                writer.WriteString("kind", reference.Kind switch { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", ContentKind.ShaderPack => "shaderpack", _ => throw new ArgumentException("Unknown kind.") });
                writer.WriteString("name", reference.Name);
                writer.WriteString("filename", reference.Filename);
                writer.WriteString("source", reference.Source switch { InventorySource.Packwiz => "packwiz", InventorySource.Local => "local", InventorySource.Builtin => "builtin", InventorySource.Missing => "missing", _ => throw new ArgumentException("Unknown source.") });
                writer.WriteString("selector", reference.Selector);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
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
                "check inventory numbering arguments and input access permissions", null);
            return 2;
        }
    }

    private static void WritePhysical(Utf8JsonWriter writer, string name, IEnumerable<NumberedPhysical> rows)
    {
        writer.WriteStartArray(name);
        foreach (var row in rows)
        {
            writer.WriteStartObject();
            if (row.Number is { } number) writer.WriteNumber("number", number); else writer.WriteNull("number");
            writer.WritePropertyName("item");
            InventoryInspection.WritePhysical(writer, row.Item);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
