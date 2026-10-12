using Tomlyn;
using Tomlyn.Syntax;

namespace ModpackTools.Core.Data;

internal sealed class TomlDocument
{
    private readonly string source;
    public DocumentSyntax Syntax { get; }

    public TomlDocument(string text)
    {
        source = text;
        TomlInputLimits.Validate(text);
        Syntax = Toml.Parse(text);
        if (Syntax.HasErrors) throw new FormatException(Syntax.Diagnostics.ToString());
    }

    public string? String(string key, string section = "")
    {
        var values = section.Length == 0 ? Syntax.KeyValues : Syntax.Tables.FirstOrDefault(table => IsLegacySection(table, section))?.Items;
        if (values is null) return null;
        // Preserve the published Packwiz reader's accepted field spelling/string form.
        // Parsing still validates the whole document; no reflective model conversion.
        var pair = values.FirstOrDefault(pair => IsBareKey(pair.Key, key));
        return pair?.Value is StringValueSyntax value && value.Token?.Text is { } token &&
            token.StartsWith('"') && !token.StartsWith("\"\"\"", StringComparison.Ordinal) ? value.Value : null;
    }

    private bool IsLegacySection(TableSyntaxBase table, string section)
    {
        if (!IsBareKey(table.Name, section) || table.OpenBracket is null || table.CloseBracket is null) return false;
        var start = table.OpenBracket.Span.End.Offset + 1;
        var end = table.CloseBracket.Span.Start.Offset;
        if (source[start..end] != section) return false;
        var lineEnd = source.IndexOf('\n', end);
        return lineEnd >= 0 && string.IsNullOrWhiteSpace(source[(end + 1)..lineEnd]);
    }

    private static bool IsBareKey(KeySyntax? key, string expected) =>
        key is { Key: BareKeySyntax bare } && !key.DotKeys.Any() && bare.Key?.Text == expected;
}
