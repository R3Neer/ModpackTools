using System.Text.Json;
using ModpackTools.Core.Inventory;

internal static class InventoryInspection
{
    public static void WritePhysical(Utf8JsonWriter writer, InventoryItem item)
    {
        writer.WriteStartObject();
        writer.WriteString("id", item.Id);
        writer.WriteString("kind", item.Kind switch { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", ContentKind.ShaderPack => "shaderpack", _ => throw new ArgumentException("Unknown content kind.") });
        writer.WriteString("name", item.Name);
        writer.WriteString("filename", item.Filename);
        writer.WriteString("side", item.Side);
        writer.WriteString("source", item.Source == ContentSource.Packwiz ? "packwiz" : "local");
        writer.WriteString("metadata_path", item.MetadataPath);
        writer.WriteString("category", item.Category);
        if (item.Kind == ContentKind.Mod) writer.WriteString("invalid_category", item.InvalidCategory);
        if (item.Source == ContentSource.Packwiz)
        {
            writer.WriteString("version_id", item.VersionId);
            writer.WriteBoolean("pinned", item.Pinned!.Value);
        }
        writer.WriteEndObject();
    }
}
