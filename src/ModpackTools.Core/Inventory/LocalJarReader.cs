using System.Collections.Immutable;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Inventory;

public sealed record LocalModObservation(LocalContentItem Item, ArtifactReadStatus Status, string? Diagnostic);
public sealed record LocalJarReadLimits(long MaximumMetadataBytes = 64 * 1024 * 1024,
    int MaximumFiles = 100000, int MaximumDirectories = 100000, ArchiveReadLimits? Archive = null)
{
    internal void Validate()
    {
        ReadBudget.Validate(new(MaximumMetadataBytes, MaximumFiles, MaximumDirectories));
        (Archive ?? new()).Validate();
    }
}

public static class LocalJarReader
{
    public static ImmutableArray<LocalModObservation> Read(ProjectContext project,
        ImmutableArray<ManagedContentItem> knownItems, LocalJarReadLimits? limits = null)
    {
        limits ??= new();
        var budget = new ReadBudget(new(limits.MaximumMetadataBytes, limits.MaximumFiles, limits.MaximumDirectories), "local-mod");
        var archiveLimits = limits.Archive ?? new();
        archiveLimits.Validate();
        var root = Path.Combine(project.Root, "mods");
        if (!Directory.Exists(root)) return [];
        var known = knownItems.Where(item => item.Kind == ContentKind.Mod)
            .Select(item => Path.GetRelativePath(root, Path.Join(Path.GetDirectoryName(item.MetadataPath), item.Filename)).Replace('\\', '/'))
            .ToHashSet(StringComparer.InvariantCultureIgnoreCase);
        var result = ImmutableArray.CreateBuilder<LocalModObservation>();
        foreach (var file in ContentFiles.Enumerate(root, budget, "*.jar"))
        {
            var relative = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
            if (known.Contains(relative)) continue;
            var metadata = JarDisplayReader.Read(file.FullName, archiveLimits, count => budget.Text(file.FullName, count));
            var item = new LocalContentItem("local:mods/" + relative.ToLowerInvariant(), ContentKind.Mod,
                metadata.Name, relative, metadata.Side);
            result.Add(new(item, metadata.Status, metadata.Diagnostic));
        }
        return result.ToImmutable();
    }
}
