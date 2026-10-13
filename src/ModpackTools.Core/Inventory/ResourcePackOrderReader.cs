using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Inventory;

public sealed record ResourceOrderReadLimits(int MaximumItems = 100000);

public static partial class ResourcePackOrderReader
{
    public static ImmutableArray<string> Read(ProjectContext project, ResourceOrderReadLimits? limits = null)
    {
        var maximum = (limits ?? new()).MaximumItems;
        if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(limits), "Reader budgets must be positive.");
        var path = Path.Combine(project.Root, "config", "defaultoptions-common.toml");
        if (!File.Exists(path)) return [];
        try
        {
            var text = TextFile.Read(path);
            if (text.Length == 0) throw new FormatException("The configuration file is empty.");
            var header = ArrayHeader().Match(text);
            if (!header.Success) return [];
            var open = header.Index + header.Length - 1;
            var close = ClosingBracket(text, open);
            if (close < 0)
                throw new DomainException("ResourcePack.UnclosedTomlArray", "TOML array 'defaultResourcePacks' is not closed",
                    "repair the Default Options configuration", "InvalidData", "defaultResourcePacks");
            var content = text.Substring(open + 1, close - open - 1);
            var ids = new List<string>();
            foreach (Match match in ArrayStrings().Matches(content))
            {
                if (ids.Count == maximum)
                    throw new DomainException("Content.ReadLimitExceeded", "The default resource order exceeds the matched-ID budget",
                        "reduce the configured resource count or split the project before retrying", "LimitsExceeded", path);
                var raw = match.Groups["value"].Value;
                if (raw.Length == 0) throw new FormatException("A quoted resource ID is empty.");
                ids.Add(Regex.Unescape(raw));
            }
            ids.Reverse();
            return ids.ToImmutableArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            throw new DomainException("ResourcePack.InvalidDefaultOptions", $"Default Options configuration '{path}' could not be decoded",
                "repair config/defaultoptions-common.toml and check its access permissions", "InvalidData", path, details: exception.Message);
        }
    }

    private static int ClosingBracket(string text, int open)
    {
        var depth = 0;
        var quoted = false;
        var escaped = false;
        for (var index = open; index < text.Length; index++)
        {
            var character = text[index];
            if (escaped) { escaped = false; continue; }
            if (quoted && character == '\\') { escaped = true; continue; }
            if (character == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (character == '[') depth++;
            else if (character == ']' && --depth == 0) return index;
        }
        return -1;
    }

    [GeneratedRegex(@"^\s*defaultResourcePacks\s*=\s*\[", RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex ArrayHeader();

    [GeneratedRegex("\"(?<value>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.NonBacktracking)]
    private static partial Regex ArrayStrings();
}
