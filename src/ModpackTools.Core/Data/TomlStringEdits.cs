using System.Text;
using Tomlyn;
using Tomlyn.Syntax;

namespace ModpackTools.Core.Data;

internal sealed record TomlStringEdit(string Section, string Key, string Value);

// The parser owns structural boundaries. Replacement text is always literal.
internal static class TomlStringEdits
{
    public static string Apply(string source, IReadOnlyList<TomlStringEdit> values, int maximumCharacters)
    {
        TomlInputLimits.Validate(source);
        var syntax = Toml.Parse(source, options: TomlParserOptions.ParseOnly);
        _ = TomlDeclarationReader.ReadParsed(syntax, new());
        var wanted = values.Select(value => Join(value.Section.Replace('.', '\0'), value.Key)).ToHashSet(StringComparer.Ordinal);
        var fields = new Dictionary<string, KeyValueSyntax>(StringComparer.Ordinal);
        var sections = new Dictionary<string, int>(StringComparer.Ordinal) { [""] = 0 };
        foreach (var field in syntax.KeyValues) AddField("", field);
        foreach (var table in syntax.Tables)
        {
            if (table is TableArraySyntax) continue;
            var section = Names(table.Name);
            var end = table.CloseBracket!.Span.Offset + table.CloseBracket.Span.Length;
            var newline = source.IndexOf('\n', end);
            sections.TryAdd(section, newline < 0 ? source.Length : newline + 1);
            foreach (var field in table.Items) AddField(section, field);
        }
        var edits = new List<(int Start, int Length, string Text)>();
        var insertions = new Dictionary<int, StringBuilder>();
        var missing = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var line = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        foreach (var value in values)
        {
            var section = value.Section.Replace('.', '\0');
            var encoded = Encode(value.Value, maximumCharacters);
            var key = Join(section, value.Key);
            if (fields.TryGetValue(key, out var field))
            {
                var span = field.Value!.Span; edits.Add((span.Offset, span.Length, encoded));
            }
            else
            {
                if (!missing.TryGetValue(section, out var builder)) missing.Add(section, builder = new());
                Append(builder, value.Key + " = " + encoded + line, maximumCharacters);
            }
        }
        foreach (var (section, content) in missing)
        {
            var known = sections.TryGetValue(section, out var position);
            if (!known) position = source.Length;
            if (!insertions.TryGetValue(position, out var builder)) insertions.Add(position, builder = new());
            if (position > 0 && source[position - 1] != '\n' && builder.Length == 0) Append(builder, line, maximumCharacters);
            if (!known) Append(builder, line + "[" + section.Replace('\0', '.') + "]" + line, maximumCharacters);
            Append(builder, content.ToString(), maximumCharacters);
        }
        foreach (var (position, content) in insertions) edits.Add((position, 0, content.ToString()));
        var result = new StringBuilder(); var offset = 0;
        foreach (var edit in edits.OrderBy(edit => edit.Start))
        {
            if (edit.Start < offset) throw new FormatException("Overlapping TOML edits are unsupported.");
            Append(result, source.AsSpan(offset, edit.Start - offset), maximumCharacters);
            Append(result, edit.Text, maximumCharacters); offset = edit.Start + edit.Length;
        }
        Append(result, source.AsSpan(offset), maximumCharacters);
        var text = result.ToString();
        _ = TomlDeclarationReader.Read(text, new());
        return text;

        void AddField(string section, KeyValueSyntax field)
        {
            var key = Join(section, Names(field.Key));
            if (wanted.Contains(key) && !fields.TryAdd(key, field)) throw new FormatException("Ambiguous TOML edit destination.");
        }
    }

    private static string Join(string section, string key) => section.Length == 0 ? key : section + '\0' + key;
    private static string Names(KeySyntax? key) => key is null ? throw new FormatException("Missing TOML key.")
        : string.Join('\0', new[] { Name(key.Key) }.Concat(key.DotKeys.Select(part => Name(part.Key))));
    private static string Name(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax { Key.Text: { } text } => text,
        StringValueSyntax { Value: { } text } => text,
        _ => throw new FormatException("Unsupported TOML key.")
    };
    private static string Encode(string value, int maximum)
    {
        var result = new StringBuilder(); Append(result, "\"", maximum);
        foreach (var character in value)
        {
            var text = character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                < ' ' or '\u007f' => "\\u" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
                _ => null
            };
            if (text is not null) Append(result, text, maximum);
            else
            {
                if (result.Length >= maximum) throw new FormatException("Generated TOML exceeds its character limit.");
                result.Append(character);
            }
        }
        Append(result, "\"", maximum); return result.ToString();
    }
    private static void Append(StringBuilder result, ReadOnlySpan<char> text, int maximum)
    { if (text.Length > maximum - result.Length) throw new FormatException("Generated TOML exceeds its character limit."); result.Append(text); }
}
