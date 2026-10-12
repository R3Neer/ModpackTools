namespace ModpackTools.Core.Projects;

public sealed record ProjectContext(string Id, string Root, string? Name, string? Version,
    string OutputName, string? Minecraft, string? Loader, string? LoaderVersion,
    string IndexFile, string IndexPath);

public sealed record ProjectCatalog(string Root, ProjectContext[] Projects);
