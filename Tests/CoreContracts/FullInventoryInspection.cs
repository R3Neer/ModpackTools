using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class FullInventoryInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || arguments.Length % 2 != 0) throw new ArgumentException("Use --inspect-inventory <root> [--max-<scope> n].");
            var managed = new ManagedReadLimits();
            var local = new LocalJarReadLimits();
            var zip = new LocalZipReadLimits();
            var order = new ResourceOrderReadLimits();
            for (var index = 2; index < arguments.Length; index += 2)
            {
                var value = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--max-managed-bytes": managed = managed with { MaximumBytes = value }; break;
                    case "--max-managed-files": managed = managed with { MaximumFiles = checked((int)value) }; break;
                    case "--max-managed-directories": managed = managed with { MaximumDirectories = checked((int)value) }; break;
                    case "--max-local-bytes": local = local with { MaximumMetadataBytes = value }; break;
                    case "--max-local-files": local = local with { MaximumFiles = checked((int)value) }; break;
                    case "--max-local-directories": local = local with { MaximumDirectories = checked((int)value) }; break;
                    case "--max-entry-bytes": local = local with { Archive = new ArchiveReadLimits(MaximumMetadataBytes: value) }; break;
                    case "--max-zip-files": zip = zip with { MaximumFiles = checked((int)value) }; break;
                    case "--max-order-items": order = order with { MaximumItems = checked((int)value) }; break;
                    default: throw new ArgumentException("Unknown inventory-inspection option.");
                }
            }
            var snapshot = InventoryReader.Read(ProjectReader.Read(arguments[1]), new(managed, local, zip, order));
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("project");
                writer.WriteString("id", snapshot.Project.Id);
                writer.WriteString("name", snapshot.Project.Name);
                writer.WriteString("version", snapshot.Project.Version);
                writer.WriteString("minecraft", snapshot.Project.Minecraft);
                writer.WriteString("loader", snapshot.Project.Loader);
                writer.WriteString("loader_version", snapshot.Project.LoaderVersion);
                writer.WriteString("root", snapshot.Project.Root);
                writer.WriteString("output_name", snapshot.Project.OutputName);
                writer.WriteString("index_file", snapshot.Project.IndexFile);
                writer.WriteString("index_path", snapshot.Project.IndexPath);
                writer.WriteEndObject();
                writer.WritePropertyName("metadata");
                EditorialInspection.WriteData(writer, snapshot.Metadata.Data);
                WriteItems(writer, "mods", snapshot.Mods);
                WriteItems(writer, "resources", snapshot.Resources);
                writer.WriteStartArray("active");
                foreach (var item in snapshot.ResourceView.Active) ResourceViewInspection.WriteActive(writer, item);
                writer.WriteEndArray();
                WriteItems(writer, "inactive", snapshot.ResourceView.Inactive);
                WriteItems(writer, "shaders", snapshot.Shaders);
                writer.WriteStartArray("local_mod_observations");
                foreach (var observation in snapshot.LocalModObservations)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", observation.Item.Id);
                    writer.WriteString("filename", observation.Item.Filename);
                    writer.WriteString("original_name", observation.Item.Name);
                    writer.WriteString("original_side", observation.Item.Side);
                    writer.WriteString("status", observation.Status.ToString().ToLowerInvariant());
                    writer.WriteString("diagnostic", observation.Diagnostic);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            bytes.Position = 0;
            bytes.CopyTo(Console.OpenStandardOutput());
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
                "check inventory inspection arguments and input access permissions", null);
            return 2;
        }
    }

    private static void WriteItems(Utf8JsonWriter writer, string name, IEnumerable<InventoryItem> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items) InventoryInspection.WritePhysical(writer, item);
        writer.WriteEndArray();
    }
}
