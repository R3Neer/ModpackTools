using System.Text;

namespace ModpackTools.Core.Data;

internal static class PsdDataWriter
{
    public static string Write(PsdTable table, int maximumCharacters)
    {
        var output = new StringBuilder(); var nodes = 0;
        Value(table, 0); Append("\r\n");
        var result = output.ToString();
        if (!Equal(table, PsdReader.Parse(result))) throw new FormatException("PSD1 value cannot be serialized without changing its data shape.");
        return result;

        void Append(string text)
        { if (text.Length > maximumCharacters - output.Length) throw new FormatException("Generated PSD1 exceeds its character limit."); output.Append(text); }
        void String(string value)
        {
            Append("'");
            foreach (var character in value)
            {
                if (character == '\'') Append("''");
                else
                {
                    if (output.Length >= maximumCharacters) throw new FormatException("Generated PSD1 exceeds its character limit.");
                    output.Append(character);
                }
            }
            Append("'");
        }
        void Value(PsdValue value, int depth)
        {
            if (++nodes > 100000 || depth > 64) throw new FormatException("Generated PSD1 exceeds its structural limit.");
            var pad = new string(' ', depth * 4);
            switch (value)
            {
                case PsdTable map:
                    Append("@{\r\n");
                    foreach (var (key, item) in map.Entries.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase).ThenBy(pair => pair.Key, StringComparer.Ordinal))
                    { Append(pad + "    "); String(key); Append(" = "); Value(item, depth + 1); Append("\r\n"); }
                    Append(pad + "}"); break;
                case PsdArray array:
                    Append("@(");
                    if (!array.Items.IsEmpty)
                    {
                        Append("\r\n");
                        for (var index = 0; index < array.Items.Length; index++)
                        {
                            Append(pad + "    "); Value(array.Items[index], depth + 1);
                            if (index + 1 < array.Items.Length) Append(",");
                            Append("\r\n");
                        }
                        Append(pad);
                    }
                    Append(")"); break;
                case PsdString text: String(text.Value); break;
                case PsdNumber number: Append(number.Literal); break;
                case PsdBoolean boolean: Append(boolean.Value ? "$true" : "$false"); break;
                case PsdNull: Append("$null"); break;
                default: throw new FormatException("Unsupported PSD1 data value.");
            }
        }
    }

    private static bool Equal(PsdValue left, PsdValue right) => (left, right) switch
    {
        (PsdTable first, PsdTable second) => first.Entries.Count == second.Entries.Count && first.Entries.All(entry => second.Entries.TryGetValue(entry.Key, out var value) && Equal(entry.Value, value)),
        (PsdArray first, PsdArray second) => first.Items.Length == second.Items.Length && first.Items.Zip(second.Items).All(pair => Equal(pair.First, pair.Second)),
        _ => left == right
    };
}
