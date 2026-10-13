using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class SelectionInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || arguments.Length % 2 != 0)
                throw new ArgumentException("Use --inspect-selected-inventory <root> [--filter value].");
            var query = new RawInventoryQuery();
            for (var index = 2; index < arguments.Length; index += 2)
                query = arguments[index] switch
                {
                    "--type" => query with { Type = arguments[index + 1] },
                    "--category" => query with { Category = arguments[index + 1] },
                    "--side" => query with { Side = arguments[index + 1] },
                    "--source" => query with { Source = arguments[index + 1] },
                    "--state" => query with { State = arguments[index + 1] },
                    "--match" => query with { Match = arguments[index + 1] },
                    _ => throw new ArgumentException("Unknown selection-inspection option.")
                };
            var view = InventorySelector.Select(InventoryReader.Read(ProjectReader.Read(arguments[1])), query);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartObject();
            WritePhysical(writer, "mods", view.Mods);
            writer.WriteStartArray("active");
            foreach (var item in view.ActiveResources) ResourceViewInspection.WriteActive(writer, item);
            writer.WriteEndArray();
            WritePhysical(writer, "inactive", view.InactiveResources);
            WritePhysical(writer, "shaders", view.Shaders);
            writer.WriteStartArray("included_types");
            foreach (var kind in view.IncludedTypes)
                writer.WriteStringValue(kind switch { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", ContentKind.ShaderPack => "shaderpack", _ => throw new ArgumentException("Unknown kind.") });
            writer.WriteEndArray();
            writer.WriteStartArray("filters");
            foreach (var label in view.Filters) writer.WriteStringValue(label);
            writer.WriteEndArray();
            writer.WriteNumber("total_matches", view.TotalMatches);
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
                "check inventory selection arguments and input access permissions", null);
            return 2;
        }
    }

    private static void WritePhysical(Utf8JsonWriter writer, string name, IEnumerable<InventoryItem> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items) InventoryInspection.WritePhysical(writer, item);
        writer.WriteEndArray();
    }
}
