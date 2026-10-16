using System.Globalization;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public sealed record ReferenceCacheWriteLimits(long MaximumBytes = 64 * 1024 * 1024);
public sealed record ReferenceCacheWriteResult(string Path, int Count);

public static class InventoryReferenceCache
{
    public static ReferenceCacheWriteResult Write(string configurationDirectory, NumberedInventorySelection inventory,
        DateTimeOffset createdUtc, ReferenceCacheWriteLimits? limits = null)
    {
        var maximumBytes = (limits ?? new()).MaximumBytes;
        if (maximumBytes <= 0)
            throw new DomainException("Inventory.InvalidReferenceCacheWritePolicy", "Reference-cache byte quota must be positive",
                "use a positive reference-cache byte quota", target: nameof(ReferenceCacheWriteLimits.MaximumBytes));
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            throw new ArgumentException("A configuration directory is required.", nameof(configurationDirectory));
        var directory = Path.GetFullPath(configurationDirectory);
        var path = Path.Combine(directory, "last-inventory.json");
        var timestamp = createdUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        foreach (var reference in inventory.References) { _ = Kind(reference.Kind); _ = Source(reference.Source); }
        AtomicJsonCacheFile.Write(directory, path, maximumBytes, writer =>
        {
            void Text(string field, string value)
            {
                writer.WriteString(field, value);
                if (writer.BytesPending >= 16 * 1024) writer.Flush();
            }
            writer.WriteStartObject();
            writer.WriteNumber("SchemaVersion", 1);
            Text("CreatedUtc", timestamp);
            Text("ProjectId", inventory.Selection.Project.Id);
            writer.WriteStartArray("Results");
            foreach (var reference in inventory.References)
            {
                writer.WriteStartObject();
                writer.WriteNumber("Index", reference.Index);
                Text("Id", reference.Id);
                Text("Kind", Kind(reference.Kind));
                Text("Name", reference.Name);
                Text("Filename", reference.Filename);
                Text("Source", Source(reference.Source));
                Text("Selector", reference.Selector);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }, () => new DomainException("Inventory.ReferenceCacheWriteLimitExceeded", "Inventory references exceed the serialized cache byte quota",
            "run modpack content list with narrower filters", "LimitsExceeded", path),
            details => new DomainException("Inventory.ReferenceCacheWriteFailed", $"Inventory reference cache '{path}' could not be written",
                "check configuration-directory permissions and retry modpack content list", "WriteError", path, details: details));
        return new(path, inventory.References.Length);
    }

    private static string Kind(ContentKind kind) => kind switch
    {
        ContentKind.Mod => "mod",
        ContentKind.ResourcePack => "resourcepack",
        ContentKind.ShaderPack => "shaderpack",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static string Source(InventorySource source) => source switch
    {
        InventorySource.Packwiz => "packwiz",
        InventorySource.Local => "local",
        InventorySource.Builtin => "builtin",
        InventorySource.Missing => "missing",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}
