using System.Collections.Immutable;
using ModpackTools.Core.Artifacts;
using Tomlyn;
using Tomlyn.Syntax;

namespace ModpackTools.Core.Data;

internal static class TomlDeclarationReader
{
    public static TomlFields Read(string text, ForgeManifestLimits limits)
    {
        try { TomlInputLimits.Validate(text); }
        catch (FormatException exception) when (exception.Message.Contains("nesting limit", StringComparison.Ordinal))
        { throw new ManifestLimitException(nameof(limits.MaximumTomlDepth)); }
        catch (FormatException exception) when (exception.Message.Contains("structural token limit", StringComparison.Ordinal))
        { throw new ManifestLimitException("HardTomlStructuralTokens"); }
        var syntax = Toml.Parse(text, options: TomlParserOptions.ParseOnly);
        Guard(syntax, limits);
        if (syntax.HasErrors) throw new FormatException(syntax.Diagnostics.ToString());
        var reader = new Builder(limits);
        var root = reader.Table(1);
        foreach (var field in syntax.KeyValues) reader.Field(root, field);
        foreach (var section in syntax.Tables)
        {
            var names = Names(section.Name);
            var parent = root;
            for (var index = 0; index < names.Length - 1; index++) parent = reader.Descend(parent, names[index]);
            TableNode target;
            if (section is TableArraySyntax)
            {
                if (!parent.Fields.TryGetValue(names[^1], out var existing))
                { existing = reader.Array(parent.Depth + 1, true); parent.Fields.Add(names[^1], existing); }
                if (existing is not ArrayNode { IsTable: true } group) throw new FormatException("Invalid TOML table-array ancestry.");
                target = reader.Table(group.Depth + 1); group.Items.Add(target);
            }
            else target = reader.Descend(parent, names[^1]);
            foreach (var field in section.Items) reader.Field(target, field);
        }
        // Construct bounded ancestry before the library's semantic validator can
        // recurse through implicit dotted tables or table-array ownership.
        Toml.Validate(syntax);
        if (syntax.HasErrors) throw new FormatException(syntax.Diagnostics.ToString());
        return (TomlFields)root.Freeze();
    }
    private static void Guard(DocumentSyntax syntax, ForgeManifestLimits limits)
    {
        var stack = new Stack<(SyntaxNode Node, int Child)>();
        var count = 0;
        Visit(syntax);
        while (stack.TryPop(out var frame))
        {
            if (frame.Child >= frame.Node.ChildrenCount) continue;
            stack.Push((frame.Node, frame.Child + 1));
            var child = frame.Node.GetChild(frame.Child);
            if (child is not null) Visit(child);
        }
        void Visit(SyntaxNode node)
        {
            if (++count > limits.MaximumSyntaxNodes) throw new ManifestLimitException(nameof(limits.MaximumSyntaxNodes));
            if (node is SyntaxToken token && token.Text?.Length > limits.Declarations.MaximumTokenCharacters)
                throw new ManifestLimitException(nameof(limits.Declarations.MaximumTokenCharacters));
            if (node is KeySyntax key && key.DotKeys.ChildrenCount + 1 > limits.MaximumTomlDepth)
                throw new ManifestLimitException(nameof(limits.MaximumTomlDepth));
            stack.Push((node, 0));
        }
    }
    private static ImmutableArray<string> Names(KeySyntax? key) => key is null ? throw new FormatException("Missing TOML key.")
        : [Key(key.Key), .. key.DotKeys.Select(item => Key(item.Key))];
    private static string Key(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax { Key.Text: { } text } => text,
        StringValueSyntax { Value: { } text } => text,
        _ => throw new FormatException("Unsupported TOML key syntax.")
    };
    private abstract class Node(int depth)
    { public int Depth { get; } = depth; public abstract TomlDeclarationValue Freeze(); }
    private sealed class ScalarNode(int depth, TomlDeclarationValue value) : Node(depth)
    { public override TomlDeclarationValue Freeze() => value; }
    private sealed class TableNode(int depth) : Node(depth)
    {
        public Dictionary<string, Node> Fields { get; } = new(StringComparer.Ordinal);
        public override TomlDeclarationValue Freeze() => new TomlFields([.. Fields.Select(entry => KeyValuePair.Create(entry.Key, entry.Value.Freeze()))]);
    }
    private sealed class ArrayNode(int depth, bool table) : Node(depth)
    {
        public bool IsTable { get; } = table;
        public List<Node> Items { get; } = [];
        public override TomlDeclarationValue Freeze() => new TomlSequence([.. Items.Select(item => item.Freeze())], IsTable);
    }
    private sealed class Builder(ForgeManifestLimits limits)
    {
        private int nodes;
        private void Allocate(int depth)
        {
            if (depth > limits.MaximumTomlDepth) throw new ManifestLimitException(nameof(limits.MaximumTomlDepth));
            if (++nodes > limits.Declarations.MaximumNodes) throw new ManifestLimitException(nameof(limits.Declarations.MaximumNodes));
        }
        public TableNode Table(int depth) { Allocate(depth); return new(depth); }
        public ArrayNode Array(int depth, bool table) { Allocate(depth); return new(depth, table); }
        public TableNode Descend(TableNode table, string key)
        {
            if (!table.Fields.TryGetValue(key, out var value))
            { value = Table(table.Depth + 1); table.Fields.Add(key, value); }
            return value switch
            {
                TableNode child => child,
                ArrayNode { IsTable: true, Items.Count: > 0 } group => (TableNode)group.Items[^1],
                _ => throw new FormatException("Invalid TOML table ancestry at '" + key + "'.")
            };
        }
        public void Field(TableNode table, KeyValueSyntax? field)
        {
            if (field is null) throw new FormatException("Missing TOML field.");
            var names = Names(field.Key);
            for (var index = 0; index < names.Length - 1; index++) table = Descend(table, names[index]);
            table.Fields.Add(names[^1], Value(field.Value, table.Depth + 1));
        }
        private Node Value(ValueSyntax? syntax, int depth)
        {
            if (syntax is ArraySyntax array)
            { var result = Array(depth, false); foreach (var item in array.Items) result.Items.Add(Value(item.Value, depth + 1)); return result; }
            if (syntax is InlineTableSyntax inline)
            { var result = Table(depth); foreach (var item in inline.Items) Field(result, item.KeyValue); return result; }
            Allocate(depth);
            return new ScalarNode(depth, syntax switch
            {
                StringValueSyntax { Value: { } text } => new TomlText(text),
                IntegerValueSyntax integer => new TomlInteger(integer.Value),
                FloatValueSyntax number => new TomlFloat(number.Value),
                BooleanValueSyntax boolean => new TomlBoolean(boolean.Value),
                DateTimeValueSyntax date => new TomlDateText(date.Value.ToString(), date.Value.DateTime,
                    date.Value.SecondPrecision, (int)date.Value.Kind, date.Value.Kind.ToString()),
                _ => throw new FormatException("Unsupported TOML declaration syntax.")
            });
        }
    }
}
