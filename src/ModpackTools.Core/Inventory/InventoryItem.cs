namespace ModpackTools.Core.Inventory;

public enum ContentSource { Packwiz, Local }

public sealed record InventoryItem(string Id, ContentKind Kind, string Name, string? Filename,
    string Side, ContentSource Source, string? VersionId, string? MetadataPath, bool? Pinned,
    string? Category = null, string? InvalidCategory = null)
{
    public static InventoryItem From(ManagedContentItem item) => new(item.Id, item.Kind, item.Name,
        item.Filename, item.Side, ContentSource.Packwiz, item.VersionId, item.MetadataPath, item.Pinned);

    public static InventoryItem From(LocalContentItem item) => new(item.Id, item.Kind, item.Name,
        item.Filename, item.Side, ContentSource.Local, null, null, null);
}
