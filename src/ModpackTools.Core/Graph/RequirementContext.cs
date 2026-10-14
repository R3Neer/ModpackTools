using System.Collections.Frozen;
using System.Collections.Immutable;

namespace ModpackTools.Core.Graph;

public sealed record RequirementProject(string Id, string VersionId);
public sealed record RequirementModVersions(string Id, ImmutableArray<string?> Versions);
public sealed record RequirementContextLimits(int MaximumProjects = 100000, int MaximumModKeys = 100000,
    int MaximumVersions = 1000000, int MaximumTextCharacters = 16 * 1024 * 1024, int MaximumRows = 200000);

public sealed class RequirementContext
{
    internal FrozenDictionary<string, string> Projects { get; }
    internal FrozenDictionary<string, ImmutableArray<string?>> Mods { get; }
    private RequirementContext(Dictionary<string, string> projects, Dictionary<string, List<string?>> mods)
    {
        Projects = projects.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Mods = mods.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    }
    public static RequirementContext Create(ImmutableArray<RequirementProject> projects,
        ImmutableArray<RequirementModVersions> mods, RequirementContextLimits? limits = null)
    {
        limits ??= new();
        var invalid = limits.MaximumProjects <= 0 ? nameof(limits.MaximumProjects)
            : limits.MaximumModKeys <= 0 ? nameof(limits.MaximumModKeys)
            : limits.MaximumVersions <= 0 ? nameof(limits.MaximumVersions)
            : limits.MaximumTextCharacters <= 0 ? nameof(limits.MaximumTextCharacters)
            : limits.MaximumRows <= 0 ? nameof(limits.MaximumRows) : null;
        if (invalid is not null) throw new DomainException("Compatibility.InvalidContextPolicy", "Requirement context limits are invalid",
            "use positive context quotas", target: invalid);
        var projectCount = projects.IsDefault ? 0 : projects.Length;
        var modCount = mods.IsDefault ? 0 : mods.Length;
        Guard((long)projectCount + modCount > limits.MaximumRows, nameof(limits.MaximumRows));
        var ownedProjects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ownedMods = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
        var characters = 0; var versions = 0;
        void Text(string? value)
        {
            var length = value?.Length ?? 0;
            Guard(length > limits.MaximumTextCharacters - characters, nameof(limits.MaximumTextCharacters));
            characters += length;
        }
        if (!projects.IsDefault)
            foreach (var project in projects)
            {
                ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(project.Id); ArgumentNullException.ThrowIfNull(project.VersionId);
                if (ownedProjects.ContainsKey(project.Id)) throw new DomainException("Content.DuplicateIdentity", "Duplicate content ID '" + project.Id + "'",
                    "supply one owned project version per identity", target: project.Id);
                Guard(ownedProjects.Count >= limits.MaximumProjects, nameof(limits.MaximumProjects));
                Text(project.Id); Text(project.VersionId); ownedProjects.Add(project.Id, project.VersionId);
            }
        if (!mods.IsDefault)
            foreach (var mod in mods)
            {
                ArgumentNullException.ThrowIfNull(mod); ArgumentNullException.ThrowIfNull(mod.Id);
                Text(mod.Id);
                if (!ownedMods.TryGetValue(mod.Id, out var values))
                {
                    Guard(ownedMods.Count >= limits.MaximumModKeys, nameof(limits.MaximumModKeys));
                    values = []; ownedMods.Add(mod.Id, values);
                }
                if (mod.Versions.IsDefault) continue;
                foreach (var version in mod.Versions)
                {
                    Guard(versions >= limits.MaximumVersions, nameof(limits.MaximumVersions));
                    Text(version); versions++; values.Add(version);
                }
            }
        return new(ownedProjects, ownedMods);
    }
    private static void Guard(bool exceeded, string property)
    {
        if (exceeded) throw new DomainException("Compatibility.ContextLimitExceeded", "Requirement context exceeds its resource limits",
            "reduce context inputs or use explicit quotas", "LimitsExceeded", property, details: property + " exceeded.");
    }
}
