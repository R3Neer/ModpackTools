using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class ClassificationInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 2) throw new ArgumentException("Use --inspect-classified-mods <root>.");
            var project = ProjectReader.Read(arguments[1]);
            var metadata = EditorialMetadataReader.Read(project);
            var managed = ManagedContentReader.Read(project, ContentKind.Mod);
            var local = LocalJarReader.Read(project, managed);
            var items = managed.Select(InventoryItem.From).Concat(local.Select(value => InventoryItem.From(value.Item))).ToImmutableArray();
            var classified = ModClassification.Apply(items, metadata);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartArray();
            foreach (var item in classified)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteString("kind", "mod");
                writer.WriteString("name", item.Name);
                writer.WriteString("filename", item.Filename);
                writer.WriteString("side", item.Side);
                writer.WriteString("source", item.Source == ContentSource.Packwiz ? "packwiz" : "local");
                writer.WriteString("metadata_path", item.MetadataPath);
                writer.WriteString("category", item.Category);
                writer.WriteString("invalid_category", item.InvalidCategory);
                if (item.Source == ContentSource.Packwiz)
                {
                    writer.WriteString("version_id", item.VersionId);
                    writer.WriteBoolean("pinned", item.Pinned!.Value);
                }
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
                "check classified-mod inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
