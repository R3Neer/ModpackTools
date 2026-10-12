namespace ModpackTools.Core.Data;

internal static class TomlInputLimits
{
    public static void Validate(string source)
    {
        var depth = 0;
        Span<char> delimiters = stackalloc char[64];
        var tokens = 0;
        var quote = '\0';
        var multiline = false;
        for (var offset = 0; offset < source.Length; offset++)
        {
            var character = source[offset];
            if (quote != '\0')
            {
                if (!multiline && character is '\r' or '\n') throw new FormatException($"Unterminated TOML string at offset {offset}.");
                if (quote == '"' && character == '\\')
                {
                    if (!multiline && offset + 1 < source.Length && source[offset + 1] is '\r' or '\n')
                        throw new FormatException($"Invalid TOML string escape at offset {offset}.");
                    offset++;
                    continue;
                }
                if (character != quote) continue;
                if (multiline)
                {
                    if (offset + 2 >= source.Length || source[offset + 1] != quote || source[offset + 2] != quote) continue;
                    // A closing multiline delimiter can include one or two quotes
                    // that belong to the value. Consume the complete quote run.
                    var start = offset;
                    while (offset + 1 < source.Length && source[offset + 1] == quote) offset++;
                    if (offset - start + 1 > 5) throw new FormatException($"Invalid TOML multiline delimiter at offset {start}.");
                }
                quote = '\0';
                continue;
            }
            if (character == '#')
            {
                var newline = source.IndexOf('\n', offset);
                if (newline < 0) break;
                offset = newline;
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                multiline = offset + 2 < source.Length && source[offset + 1] == quote && source[offset + 2] == quote;
                if (multiline) offset += 2;
            }
            if (character is '[' or '{')
            {
                if (depth == delimiters.Length) throw new FormatException($"TOML exceeds the nesting limit of 64 at offset {offset}.");
                delimiters[depth++] = character;
            }
            else if (character is ']' or '}')
            {
                var expected = character == ']' ? '[' : '{';
                if (depth == 0 || delimiters[depth - 1] != expected) throw new FormatException($"Unmatched TOML delimiter at offset {offset}.");
                depth--;
            }
            if (character is '[' or '{' or '=' or ',')
                if (++tokens > 100000) throw new FormatException($"TOML exceeds the structural token limit of 100000 at offset {offset}.");
        }
        if (quote != '\0' || depth != 0) throw new FormatException("Unterminated TOML string or container.");
    }
}
