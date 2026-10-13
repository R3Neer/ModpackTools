using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class ResourceViewInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 2) throw new ArgumentException("Use --inspect-resource-view <root>.");
            var project = ProjectReader.Read(arguments[1]);
            var metadata = EditorialMetadataReader.Read(project);
            var managed = ManagedContentReader.Read(project, ContentKind.ResourcePack);
            var local = LocalZipReader.Read(project, managed, ContentKind.ResourcePack);
            var resources = managed.Select(InventoryItem.From).Concat(local.Select(InventoryItem.From)).ToImmutableArray();
            var view = ResourcePackActivation.Compose(resources, ResourcePackOrderReader.Read(project), metadata);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartObject();
            writer.WriteStartArray("resources");
            foreach (var item in resources) InventoryInspection.WritePhysical(writer, item);
            writer.WriteEndArray();
            writer.WriteStartArray("active");
            foreach (var item in view.Active) WriteActive(writer, item);
            writer.WriteEndArray();
            writer.WriteStartArray("inactive");
            foreach (var item in view.Inactive) InventoryInspection.WritePhysical(writer, item);
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
                "check resource-view inspection arguments and input access permissions", null);
            return 2;
        }
    }

    internal static void WriteActive(Utf8JsonWriter writer, ActiveResource item)
    {
        writer.WriteStartObject();
        writer.WriteString("id", item.Id);
        writer.WriteString("kind", "resourcepack");
        writer.WriteString("name", item.Name);
        writer.WriteString("filename", item.Filename);
        writer.WriteString("source", item.Source switch
        {
            ActiveResourceSource.Packwiz => "packwiz",
            ActiveResourceSource.Local => "local",
            ActiveResourceSource.Builtin => "builtin",
            ActiveResourceSource.Missing => "missing",
            _ => throw new ArgumentException("Unknown active resource source.")
        });
        writer.WriteBoolean("enabled", true);
        writer.WriteNumber("priority", item.Priority);
        writer.WriteEndObject();
    }
}
