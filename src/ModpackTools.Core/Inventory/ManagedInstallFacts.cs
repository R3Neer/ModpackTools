using System.Globalization;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public sealed record ManagedDownload(string Url, string Hash, string Algorithm);
public sealed record ManagedInstallFacts(ManagedDownload? Download, bool Optional, bool DefaultEnabled, bool Pinned);

internal static class ManagedInstallFactsReader
{
    public static ManagedInstallFacts Read(TomlFields root)
    {
        ManagedDownload? download = null;
        if (root.Get("download") is TomlFields fields)
            download = new(Text(fields.Get("url")), Text(fields.Get("hash")), Text(fields.Get("hash-format")));
        else if (root.Get("download") is not TomlMissing) throw new FormatException("Packwiz download must be a table.");
        var optional = false; var enabled = true;
        if (root.Get("option") is TomlFields option)
        {
            optional = True(option.Get("optional"));
            if (optional) enabled = True(option.Get("default"));
        }
        else if (root.Get("option") is not TomlMissing) throw new FormatException("Packwiz option must be a table.");
        return new(download, optional, enabled, True(root.Get("pin")));
    }

    private static string Text(TomlDeclarationValue value) => value switch
    {
        TomlMissing => "",
        TomlText text => text.Value,
        TomlInteger number => number.Value.ToString(CultureInfo.InvariantCulture),
        TomlBoolean boolean => boolean.Value.ToString(),
        _ => throw new FormatException("Packwiz download fields must be scalar strings.")
    };
    private static bool True(TomlDeclarationValue value) => value switch
    {
        TomlMissing => false,
        TomlBoolean boolean => boolean.Value,
        TomlInteger integer => integer.Value == 1,
        TomlFloat number => number.Value == 1,
        TomlText text => StringComparer.InvariantCultureIgnoreCase.Equals(text.Value, "True"),
        _ => throw new FormatException("Packwiz selection flags must be Boolean-compatible scalars.")
    };
}
