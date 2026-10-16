namespace ModpackTools.Core.Projects;

public static class ProjectSelection
{
    public static string EffectiveId(string? requestedId, string? activeProject)
    {
        var effectiveId = string.IsNullOrWhiteSpace(requestedId) ? activeProject : requestedId;
        if (string.IsNullOrWhiteSpace(effectiveId))
            throw new DomainException("Project.NotSelected", "No active project is selected",
                "run modpack project use <id>, or add global --project <id>", "ObjectNotFound");
        return effectiveId;
    }

    public static ProjectContext Find(ProjectCatalog catalog, string effectiveId) =>
        catalog.Projects.FirstOrDefault(project => project.Id.Equals(effectiveId, StringComparison.OrdinalIgnoreCase)) ??
        throw new DomainException("Project.NotFound", $"Project '{effectiveId}' is not registered",
            "modpack project list", "ObjectNotFound", effectiveId);
}
