namespace ModpackTools.Core;

public sealed record VersionInfo(string Version);

public static class ProductVersion
{
    public static VersionInfo Current { get; } = new("4.0.2");

    public static VersionInfo Read(string[] arguments, string? project)
    {
        if (!string.IsNullOrEmpty(project))
            throw new DomainException("Option.ForbiddenCombination", "Option '--project' cannot be used with --version", "modpack --version");
        foreach (var argument in arguments)
        {
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                continue;
            var name = argument[2..].Split('=', 2)[0];
            throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
        }
        if (arguments.Length != 0)
            throw new DomainException("Command.InvalidArguments", "The global version option does not accept arguments", "modpack --version");
        return Current;
    }
}
