using ModpackTools.Core;
using System.Text.RegularExpressions;

namespace ModpackTools.Cli;

internal sealed partial record PresentationOptions(string[] Arguments, string? Project, bool Json, bool NoHuman)
{
    [GeneratedRegex("^(?:--(color|ascii|project|json|no-human)|(-p))(?:=(.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex GlobalOption();

    public static PresentationOptions Parse(string[] tokens)
    {
        var remaining = new List<string>(tokens.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? project = null;
        var json = false;
        var noHuman = false;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index] switch
            {
                "-V" => "--version",
                "-h" => "--help",
                "-n" => "--dry-run",
                "-y" => "--yes",
                var unchanged => unchanged
            };
            var match = GlobalOption().Match(token);
            if (!match.Success)
            {
                remaining.Add(token);
                continue;
            }
            var name = match.Groups[2].Success ? "project" : match.Groups[1].Value;
            if (!seen.Add(name))
                throw new DomainException("Option.Duplicate", $"Option '--{name}' is repeated", $"specify --{name} once");
            string? value = match.Groups[3].Success ? match.Groups[3].Value : null;
            if (name is "ascii" or "json" or "no-human")
            {
                if (value is not null)
                    throw new DomainException("Option.UnexpectedValue", $"Option '--{name}' does not accept a value", $"--{name}");
                if (name == "json") json = true;
                if (name == "no-human") noHuman = true;
                continue;
            }
            if (value is null)
            {
                if (++index == tokens.Length)
                    throw new DomainException("Option.MissingValue", $"Option '--{name}' requires a value", $"--{name} <value>");
                value = tokens[index];
            }
            if (name == "project")
            {
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-'))
                    throw new DomainException("Option.MissingValue", "Option '--project' requires a project ID", "--project <id>");
                project = value;
            }
            else if (!new[] { "auto", "always", "never" }.Contains(value, StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Option.InvalidColor", $"Color mode '{value}' is invalid", "--color auto|always|never");
        }
        if (noHuman && !json)
            throw new DomainException("Option.RequiredCombination", "Option '--no-human' requires '--json'", "--json --no-human");
        return new(remaining.ToArray(), project, json, noHuman);
    }
}
