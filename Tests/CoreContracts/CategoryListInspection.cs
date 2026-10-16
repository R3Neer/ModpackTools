using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class CategoryListInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 2 && (arguments.Length != 4 || arguments[2] != "--culture"))
                throw new ArgumentException("Use --inspect-category-view <root> [--culture name].");
            if (arguments.Length == 4) CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(arguments[3]);
            var view = CategoryList.Read(ProjectReader.Read(arguments[1]));
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                Write(writer, view);
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
                "check category-view inspection arguments and input access permissions", null);
            return 2;
        }
    }
    public static void Write(Utf8JsonWriter writer, CategoryListView view)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("Project");
        writer.WriteString("Id", view.Project.Id);
        writer.WriteString("Root", view.Project.Root);
        writer.WriteString("DisplayName", view.Project.Name);
        writer.WriteString("DisplayVersion", view.Project.Version);
        writer.WriteString("OutputName", view.Project.OutputName);
        writer.WriteString("MinecraftVersion", view.Project.Minecraft);
        writer.WriteString("Loader", view.Project.Loader);
        writer.WriteString("LoaderVersion", view.Project.LoaderVersion);
        writer.WriteString("IndexFile", view.Project.IndexFile);
        writer.WriteEndObject();
        writer.WriteStartArray("Categories");
        foreach (var row in view.Categories)
        {
            writer.WriteStartObject();
            writer.WriteString("Id", row.Id);
            writer.WriteString("Name", row.Name);
            if (row.Order is { } order) writer.WriteNumber("Order", order); else writer.WriteNull("Order");
            writer.WriteNumber("ModCount", row.ModCount);
            if (row.IsUnclassified) writer.WriteBoolean("IsUnclassified", true);
            writer.WriteNumber("ReferenceNumber", row.ReferenceNumber);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteNumber("DefinedCategoryCount", view.DefinedCategoryCount);
        writer.WriteEndObject();
    }
}
