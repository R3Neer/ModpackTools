using System.Collections.Immutable;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Inventory;

public sealed record LocalContentItem(string Id, ContentKind Kind, string Name, string Filename, string Side);
public sealed record LocalZipReadLimits(int MaximumFiles = 100000);

public static class LocalZipReader
{
    public static ImmutableArray<LocalContentItem> Read(ProjectContext project,
        ImmutableArray<ManagedContentItem> knownItems, LocalZipReadLimits? limits = null) =>
        Read(project, knownItems, [("resourcepacks", ContentKind.ResourcePack), ("shaderpacks", ContentKind.ShaderPack)], limits);

    public static ImmutableArray<LocalContentItem> Read(ProjectContext project,
        ImmutableArray<ManagedContentItem> knownItems, ContentKind kind, LocalZipReadLimits? limits = null) =>
        Read(project, knownItems, [(kind switch
        {
            ContentKind.ResourcePack => "resourcepacks", ContentKind.ShaderPack => "shaderpacks",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }, kind)], limits);

    private static ImmutableArray<LocalContentItem> Read(ProjectContext project,
        ImmutableArray<ManagedContentItem> knownItems, (string Directory, ContentKind Kind)[] locations, LocalZipReadLimits? limits)
    {
        var maximum = (limits ?? new()).MaximumFiles;
        if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(limits), "Reader budgets must be positive.");
        var count = 0;
        void Matched(FileInfo file)
        {
            if (++count > maximum)
                throw new DomainException("Content.ReadLimitExceeded", "The local ZIP matched-file count exceeds the reader budget",
                    "reduce the local content count or split the project before retrying", "LimitsExceeded", file.FullName);
        }
        var result = ImmutableArray.CreateBuilder<LocalContentItem>();
        foreach (var (directory, kind) in locations)
        {
            var path = Path.Combine(project.Root, directory);
            if (!System.IO.Directory.Exists(path)) continue;
            var known = knownItems.Where(item => item.Kind == kind).Select(item => item.Filename)
                .OfType<string>().ToHashSet(StringComparer.InvariantCultureIgnoreCase);
            foreach (var file in ContentFiles.InDirectory(new(path), "*.zip", Matched))
            {
                if (known.Contains(file.Name)) continue;
                result.Add(new("local:" + directory + "/" + file.Name.ToLowerInvariant(), kind,
                    Path.GetFileNameWithoutExtension(file.Name), file.Name, kind == ContentKind.ResourcePack ? "client" : "unknown"));
            }
        }
        return result.ToImmutable();
    }
}
