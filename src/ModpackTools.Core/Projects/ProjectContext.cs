namespace ModpackTools.Core.Projects;

public sealed record ProjectContext(string Id, string Root, string? Name, string? Version,
    string OutputName, string? Minecraft, string? Loader, string? LoaderVersion,
    string IndexFile, string IndexPath)
{
    public bool JavaDeclared { get; init; }
    public string JavaVersion { get; init; } = "";
}

public sealed record ProjectCatalog(string Root, ProjectContext[] Projects);
