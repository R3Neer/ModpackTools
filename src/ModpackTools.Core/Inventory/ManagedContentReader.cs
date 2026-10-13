using System.Collections.Immutable;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Inventory;

public static class ManagedContentReader
{
    public static ImmutableArray<ManagedContentItem> Read(ProjectContext project, ManagedReadLimits? limits = null) =>
        Read(project, [("mods", ContentKind.Mod), ("resourcepacks", ContentKind.ResourcePack), ("shaderpacks", ContentKind.ShaderPack)], limits);

    public static ImmutableArray<ManagedContentItem> Read(ProjectContext project, ContentKind kind, ManagedReadLimits? limits = null) =>
        Read(project, [(kind switch
        {
            ContentKind.Mod => "mods", ContentKind.ResourcePack => "resourcepacks", ContentKind.ShaderPack => "shaderpacks",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }, kind)], limits);

    private static ImmutableArray<ManagedContentItem> Read(ProjectContext project,
        (string Directory, ContentKind Kind)[] locations, ManagedReadLimits? limits)
    {
        var budget = new ReadBudget(limits ?? new());
        var items = ImmutableArray.CreateBuilder<ManagedContentItem>();
        foreach (var (directory, kind) in locations)
            foreach (var file in ContentFiles.Enumerate(Path.Combine(project.Root, directory), budget))
            {
                try
                {
                    var text = TextFile.Read(file.FullName, out var bytes);
                    budget.Text(file.FullName, bytes);
                    if (text.Length == 0) throw new FormatException("The metadata file is empty.");
                    var document = new TomlDocument(text);
                    var relative = Path.GetRelativePath(project.Root, file.FullName).Replace('\\', '/');
                    var modrinth = document.String("mod-id", "update.modrinth");
                    var curseforge = document.String("project-id", "update.curseforge");
                    var id = !string.IsNullOrEmpty(modrinth) ? "modrinth:" + modrinth :
                        !string.IsNullOrEmpty(curseforge) ? "curseforge:" + curseforge : "packwiz:" + relative.ToLowerInvariant();
                    var name = document.String("name");
                    if (string.IsNullOrEmpty(name)) name = file.Name.EndsWith(".pw.toml", StringComparison.OrdinalIgnoreCase) ? file.Name[..^8] : file.Name;
                    var side = document.String("side");
                    side = string.IsNullOrEmpty(side) ? "unknown" : side.ToLowerInvariant();
                    items.Add(new(id, kind, name, document.String("filename"), side,
                        document.String("version", "update.modrinth"), file.FullName, document.RootPin()));
                }
                catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
                {
                    throw new DomainException("Content.InvalidMetadata", $"Managed metadata '{file.FullName}' could not be read as valid TOML",
                        "repair the Packwiz metadata file and check its access permissions", "InvalidData", file.FullName, details: exception.Message);
                }
            }
        return items.ToImmutable();
    }
}
