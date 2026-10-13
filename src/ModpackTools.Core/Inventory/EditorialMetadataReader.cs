using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Inventory;

public sealed record EditorialMetadata(PsdTable Data, PsdTable Categories, PsdTable Mods,
    PsdTable ResourcePacks, PsdTable? Content);

public static class EditorialMetadataReader
{
    private static readonly PsdTable Empty = new(ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));

    public static EditorialMetadata Read(ProjectContext project)
    {
        var path = Path.Combine(project.Root, ".modpack", "metadata.psd1");
        PsdTable source;
        try { source = File.Exists(path) ? PsdReader.Parse(TextFile.Read(path)) : Empty; }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new DomainException("Metadata.InvalidFile", $"Metadata file '{path}' is not a valid PSD1 file",
                "repair .modpack/metadata.psd1", "InvalidData", path, details: exception.Message);
        }
        var data = source.Entries;
        PsdTable Section(string name)
        {
            if (!data.TryGetValue(name, out var value)) { data = data.Add(name, Empty); return Empty; }
            return value as PsdTable ?? throw new DomainException("Metadata.InvalidSection",
                $"Section '{name}' in metadata file '{path}' must be a table", "repair .modpack/metadata.psd1", "InvalidData", name);
        }
        var categories = Section("Categories");
        var mods = Section("Mods");
        var resources = Section("ResourcePacks");
        if (data.TryGetValue("ContentSchemaVersion", out var schema) && UnsupportedSchema(schema))
            throw new DomainException("Metadata.UnsupportedContentSchema", "Content intent metadata uses an unsupported schema",
                "upgrade ModpackTools before modifying this project", "InvalidData");
        PsdTable? content = null;
        if (data.TryGetValue("Content", out var contentValue))
            content = contentValue as PsdTable ?? throw new DomainException("Metadata.InvalidContentSection",
                "Content intent metadata must be a table", "repair the Content section of metadata.psd1", "InvalidData");
        return new(new(data), categories, mods, resources, content);
    }

    private static bool UnsupportedSchema(PsdValue value)
    {
        if (value is not PsdArray array) return !EqualsOne(value);
        var survivors = array.Items.Where(item => !EqualsOne(item)).ToArray();
        return survivors.Length > 1 || survivors.Length == 1 && Truthy(survivors[0]);
    }

    private static bool EqualsOne(PsdValue value) => value switch
    {
        PsdString text => text.Value == "1",
        PsdBoolean flag => flag.Value,
        PsdNumber number when number.Literal.IndexOfAny(['.', 'e', 'E']) >= 0 =>
            double.Parse(number.Literal, CultureInfo.InvariantCulture) == 1,
        PsdNumber number => number.Literal.TrimStart('+', '0') == "1",
        _ => false
    };

    private static bool Truthy(PsdValue value)
    {
        while (value is PsdArray { Items.Length: 1 } single) value = single.Items[0];
        return value switch
        {
            PsdNull => false,
            PsdBoolean flag => flag.Value,
            PsdString text => text.Value.Length != 0,
            PsdArray array => array.Items.Length != 0,
            PsdNumber number when number.Literal.IndexOfAny(['.', 'e', 'E']) >= 0 =>
                double.Parse(number.Literal, CultureInfo.InvariantCulture) != 0,
            PsdNumber number => number.Literal.TrimStart('+', '-', '0').Length != 0,
            _ => true
        };
    }
}
