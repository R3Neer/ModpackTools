using System.Text.RegularExpressions;
using Tomlyn;

namespace ModpackTools.Core.Data;

internal sealed partial class TomlDocument
{
    private readonly string source;
    private readonly Dictionary<string, (int Start, int Length)> windows = new(StringComparer.Ordinal);
    private const string FieldStart = "^\\s*";
    private const string FieldEnd = "\\s*=\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"\\s*(?:#.*)?$";

    public TomlDocument(string text)
    {
        source = text;
        TomlInputLimits.Validate(text);
        var syntax = Toml.Parse(text);
        if (syntax.HasErrors) throw new FormatException(syntax.Diagnostics.ToString());
        windows.Add("", Window(0));
        // Published fields are a lexical projection, including apparent headers
        // inside valid multiline strings. The AST validates data, not this contract.
        foreach (Match header in SectionHeader().Matches(text))
        {
            var section = header.Groups["section"].Value;
            windows.TryAdd(section, Window(header.Index + header.Length));
        }
    }

    public string? String(string key, string section = "")
    {
        if (!windows.TryGetValue(section, out var window)) return null;
        var pattern = key switch
        {
            "name" => Name(),
            "version" => Version(),
            "filename" => Filename(),
            "side" => Side(),
            "mod-id" => ModId(),
            "project-id" => ProjectId(),
            "file" => File(),
            "minecraft" => Minecraft(),
            "fabric" => Fabric(),
            "quilt" => Quilt(),
            "neoforge" => NeoForge(),
            "forge" => Forge(),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown compatibility field")
        };
        var match = pattern.Match(source, window.Start, window.Length);
        if (!match.Success) return null;
        try { return Regex.Unescape(match.Groups["value"].Value); }
        catch (ArgumentException exception) { throw new FormatException("The string escape is unsupported by the compatibility decoder.", exception); }
    }

    public bool RootPin()
    {
        var window = windows[""];
        return Pin().Match(source, window.Start, window.Length).Success;
    }

    private (int Start, int Length) Window(int start)
    {
        var boundary = SectionBoundary().Match(source, start);
        return (start, (boundary.Success ? boundary.Index : source.Length) - start);
    }

    [GeneratedRegex(@"^\s*\[", RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex SectionBoundary();

    [GeneratedRegex(@"^\s*\[(?<section>[^\]\r\n]+)\]\s*\r?\n", RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex SectionHeader();

    [GeneratedRegex(FieldStart + "name" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Name();
    [GeneratedRegex(FieldStart + "version" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Version();
    [GeneratedRegex(FieldStart + "filename" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Filename();
    [GeneratedRegex(FieldStart + "side" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Side();
    [GeneratedRegex(FieldStart + "mod-id" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex ModId();
    [GeneratedRegex(FieldStart + "project-id" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex ProjectId();
    [GeneratedRegex(FieldStart + "file" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex File();
    [GeneratedRegex(FieldStart + "minecraft" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Minecraft();
    [GeneratedRegex(FieldStart + "fabric" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Fabric();
    [GeneratedRegex(FieldStart + "quilt" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Quilt();
    [GeneratedRegex(FieldStart + "neoforge" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex NeoForge();
    [GeneratedRegex(FieldStart + "forge" + FieldEnd, RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Forge();

    [GeneratedRegex(@"^pin[ \t]*=[ \t]*true[ \t]*(?:#.*)?\r?$", RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex Pin();
}
