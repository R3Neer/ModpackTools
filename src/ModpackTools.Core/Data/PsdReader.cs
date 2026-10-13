using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ModpackTools.Core.Data;

/// <summary>Parses data literals only. Never evaluates PowerShell expressions.</summary>
public sealed partial class PsdReader
{
    private readonly string source;
    private int position;
    private int nodes;

    private PsdReader(string source) => this.source = source;

    public static PsdTable Parse(string source)
    {
        if (source.Length > 4 * 1024 * 1024)
            throw new PsdParseException("PSD1 exceeds the 4 Mi-character limit", 0);
        var parser = new PsdReader(source);
        parser.SkipTrivia();
        var value = parser.ReadValue(0);
        parser.SkipTrivia();
        if (parser.position != source.Length || value is not PsdTable table)
            throw parser.Error("Expected a single data table");
        return table;
    }

    [GeneratedRegex(@"^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberLiteral();

    private PsdValue ReadValue(int depth)
    {
        var first = ReadAtom(depth);
        if (!TakeComma()) return first;
        var items = ImmutableArray.CreateBuilder<PsdValue>();
        items.Add(first);
        do
        {
            SkipTrivia();
            items.Add(ReadAtom(depth + 1));
        } while (TakeComma());
        return new PsdArray(items.ToImmutable());
    }

    private PsdValue ReadAtom(int depth)
    {
        if (depth > 64 || ++nodes > 100000)
            throw Error("PSD1 nesting or node limit exceeded");
        SkipTrivia();
        if (Take("@{")) return ReadTable(depth + 1);
        if (Take("@(")) return ReadArray(depth + 1);
        if (position < source.Length && source[position] is '\'' or '"')
            return new PsdString(ReadString());
        var token = ReadToken();
        if (token.Equals("$true", StringComparison.OrdinalIgnoreCase)) return new PsdBoolean(true);
        if (token.Equals("$false", StringComparison.OrdinalIgnoreCase)) return new PsdBoolean(false);
        if (token.Equals("$null", StringComparison.OrdinalIgnoreCase)) return new PsdNull();
        if (NumberLiteral().IsMatch(token))
        {
            if (token.IndexOfAny(['.', 'e', 'E']) < 0 &&
                !decimal.TryParse(token, CultureInfo.InvariantCulture, out _) &&
                !double.IsFinite(PsdNumbers.Floating(token)))
                throw Error("Integer literal exceeds the supported numeric range");
            // Keep the literal: extraction, not parsing, chooses numeric range.
            return new PsdNumber(token);
        }
        throw Error("Unsupported data expression");
    }

    private PsdTable ReadTable(int depth)
    {
        var entries = ImmutableDictionary.CreateBuilder<string, PsdValue>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            SkipTrivia();
            if (Take("}")) return new(entries.ToImmutable());
            var key = position < source.Length && source[position] is '\'' or '"' ? ReadString() : ReadKey();
            SkipTrivia();
            if (!Take("=")) throw Error("Expected '=' after data key");
            SkipTrivia();
            var value = ReadValue(depth);
            if (!entries.TryAdd(key, value)) throw Error($"Duplicate data key '{key}'");
            var newline = SkipTrivia();
            if (Take(";")) continue;
            if (position < source.Length && source[position] == '}') continue;
            if (!newline) throw Error("Expected a newline or ';' between data entries");
        }
    }

    private PsdArray ReadArray(int depth)
    {
        var items = ImmutableArray.CreateBuilder<PsdValue>();
        while (true)
        {
            SkipTrivia();
            if (Take(")")) return new(items.ToImmutable());
            var value = ReadValue(depth);
            // PowerShell array subexpressions enumerate nested array values.
            if (value is PsdArray array) items.AddRange(array.Items); else items.Add(value);
            var newline = SkipTrivia();
            if (Take(";")) continue;
            if (position < source.Length && source[position] == ')') continue;
            if (!newline) throw Error("Expected a separator between array values");
        }
    }

    private string ReadString()
    {
        var quote = source[position++];
        var result = new StringBuilder();
        while (position < source.Length)
        {
            var character = source[position++];
            if (character == quote)
            {
                if (position < source.Length && source[position] == quote)
                {
                    position++;
                    result.Append(quote);
                    continue;
                }
                return result.ToString();
            }
            if (quote == '"' && character == '$') throw Error("Interpolation is not a data literal");
            if (quote == '"' && character == '`')
            {
                if (position == source.Length) throw Error("Incomplete string escape");
                var escaped = source[position++];
                if (escaped == '\r' && position < source.Length && source[position] == '\n') { position++; continue; }
                if (escaped == '\n') continue;
                if (escaped == 'u')
                {
                    if (!Take("{")) throw Error("Expected '{' after Unicode escape");
                    var start = position;
                    while (position < source.Length && source[position] != '}') position++;
                    if (position - start is < 1 or > 6 ||
                        !int.TryParse(source.AsSpan(start, position - start), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) ||
                        !Rune.TryCreate(codePoint, out var rune) || !Take("}"))
                        throw Error("Invalid Unicode escape");
                    result.Append(rune.ToString());
                    continue;
                }
                character = escaped switch
                {
                    '0' => '\0',
                    'a' => '\a',
                    'b' => '\b',
                    'e' => '\x1b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'v' => '\v',
                    _ => escaped
                };
            }
            result.Append(character);
        }
        throw Error("Unclosed data string");
    }

    private string ReadKey()
    {
        if (position == source.Length || (!char.IsLetter(source[position]) && source[position] != '_'))
            throw Error("Bare data keys must be identifiers; quote string keys");
        var start = position;
        while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] is '_' or '-')) position++;
        if (position == start) throw Error("Expected a data key");
        return source[start..position];
    }

    private string ReadToken()
    {
        var start = position;
        while (position < source.Length && !char.IsWhiteSpace(source[position]) && source[position] is not (';' or ',' or '}' or ')' or '#')) position++;
        if (position == start) throw Error("Expected a data value");
        return source[start..position];
    }

    private bool SkipTrivia()
    {
        var newline = false;
        while (position < source.Length)
        {
            if (char.IsWhiteSpace(source[position]) || source[position] == '\ufeff')
            {
                newline |= source[position] is '\r' or '\n';
                position++;
            }
            else if (Take("<#"))
            {
                while (true)
                {
                    if (position == source.Length) throw Error("Unclosed block comment");
                    if (Take("<#")) throw Error("Nested block comments are not supported by PSD1");
                    else if (Take("#>")) break;
                    else { position++; }
                }
            }
            else if (source[position] == '#')
            {
                while (position < source.Length && source[position] is not ('\r' or '\n')) position++;
            }
            else break;
        }
        return newline;
    }

    private bool TakeComma()
    {
        var saved = position;
        var newline = SkipTrivia();
        if (!newline && Take(",")) return true;
        position = saved;
        return false;
    }

    private bool Take(string token)
    {
        if (!source.AsSpan(position).StartsWith(token, StringComparison.Ordinal)) return false;
        position += token.Length;
        return true;
    }

    private PsdParseException Error(string message) => new(message, position);
}
