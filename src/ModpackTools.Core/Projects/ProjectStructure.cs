namespace ModpackTools.Core.Projects;

public static class ProjectStructure
{
    public static void Assert(ProjectContext project)
    {
        ArgumentNullException.ThrowIfNull(project);
        foreach (var relative in new[] { "pack.toml", project.IndexFile, ".modpack/project.psd1", ".modpack/metadata.psd1" })
        {
            var path = Path.GetFullPath(Path.Combine(project.Root, relative));
            if (!File.Exists(path)) throw new DomainException("Project.Incomplete",
                $"Project '{project.Id}' is incomplete because '{relative}' is missing", "restore the missing file or recreate the project",
                "InvalidData", path, details: $"Project root: '{project.Root}'");
        }
    }
}
