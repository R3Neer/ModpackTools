using System.Collections.Immutable;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Versions;
using static ModpackTools.Core.Graph.ProjectHealth;

namespace ModpackTools.Core.Graph;

internal static class ContentNodeReader
{
    public static async Task<ProjectContentNode> ReadAsync(ProjectContext project, InventoryItem item, ProviderVersion? version,
        bool check, ArtifactProvider artifacts, Func<string, Task<ProviderVersion?>> readVersion,
        GraphReportLimits graphPolicy, ArtifactTraversalLimits traversal, ContentNodeBudget budget,
        ImmutableArray<string> initialWarnings, EditorialMetadata? metadata = null, string? intentOverride = null)
    {
        var warnings = initialWarnings.ToBuilder();
        void Warn(string value) { budget.Warning(value); warnings.Add(value); }
        ProviderFile? selected = null;
        var requirements = ImmutableArray.CreateBuilder<ManifestRequirement>();
        if (version is not null)
        {
            selected = version.Files.FirstOrDefault(file => file.Primary) ?? version.Files.FirstOrDefault();
            if (item.MetadataPath is not null && StringComparer.InvariantCulture.Equals(version.Id, item.VersionId))
                selected = version.Files.FirstOrDefault(file => StringComparer.InvariantCulture.Equals(file.Filename, item.Filename)) ?? selected;
            foreach (var dependency in version.Dependencies)
            {
                var kind = dependency.Type.ToLowerInvariant();
                if (kind is not ("required" or "incompatible")) continue;
                var id = dependency.ProjectId;
                if (id.Length == 0 && dependency.VersionId.Length != 0)
                {
                    try { id = (await readVersion(dependency.VersionId).ConfigureAwait(false))?.ProjectId ?? ""; }
                    catch (DomainException error) when (Recoverable(error)) { Warn(WarningText(error)); }
                }
                if (id.Length == 0) { Warn("A provider dependency has no verifiable project identity"); continue; }
                var requirement = new ManifestRequirement("modrinth:" + id, VersionConstraint.Text("*"), kind, "both", "project",
                    VersionScheme.General, "provider", dependency.VersionId, [], [], null);
                budget.CountRequirement(requirement); requirements.Add(requirement);
            }
        }
        else if (item.Source == ContentSource.Packwiz) Warn("Provider dependency metadata is unavailable");
        var mods = ImmutableArray<ArtifactMod>.Empty;
        if (item.Kind == ContentKind.Mod)
        {
            try
            {
                ArtifactArchiveDeclarations? observed = null;
                var nodeTraversal = traversal with
                {
                    MaximumMods = Math.Min(traversal.MaximumMods, Math.Max(1, graphPolicy.MaximumMods - budget.Mods)),
                    MaximumRequirements = Math.Min(traversal.MaximumRequirements, Math.Max(1, graphPolicy.MaximumRequirements - budget.Requirements)),
                    MaximumAliases = Math.Min(traversal.MaximumAliases, Math.Max(1, graphPolicy.MaximumAliases - budget.Aliases)),
                    MaximumDiagnostics = Math.Min(traversal.MaximumDiagnostics, Math.Max(1, graphPolicy.MaximumWarnings - budget.Warnings))
                };
                if (item.Source == ContentSource.Local)
                {
                    var local = Contained(project.Root, "mods/" + item.Filename);
                    using var file = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read);
                    observed = ArtifactArchiveReader.Read(file, local, project.Loader ?? "", nodeTraversal);
                }
                else
                {
                    ArtifactRequest? request = selected is not null
                        ? new(selected.Url, selected.Sha512.Length != 0 ? selected.Sha512 : selected.Sha1,
                            selected.Sha512.Length != 0 ? "sha512" : "sha1", Check: check)
                        : item.InstallFacts?.Download is { } download
                            ? new(download.Url, download.Hash, download.Algorithm,
                                Contained(Path.GetDirectoryName(item.MetadataPath!)!, item.Filename ?? ""), check) : null;
                    if (request is not null) observed = (await artifacts.ReadAsync(request, project.Loader ?? "", nodeTraversal).ConfigureAwait(false)).Declarations;
                }
                if (observed is null) Warn("JAR requirements have not been verified; run content list --verify");
                else
                {
                    if (observed.Status == ArtifactTraversalStatus.Limited) throw new DomainException("Metadata.ArchiveLimit",
                        "Artifact declarations exceed their resource limit", "use explicit bounded archive quotas", "LimitsExceeded", item.Id);
                    foreach (var mod in observed.Mods) budget.Mod(mod);
                    mods = observed.Mods;
                    foreach (var diagnostic in observed.Diagnostics) Warn(diagnostic.Diagnostic.Message);
                }
            }
            catch (DomainException error) when (Recoverable(error)) { Warn(WarningText(error)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Warn(error.Message); }
        }
        var facts = item.InstallFacts;
        if (facts?.Optional == true) Warn("Optional install combinations require separate verification; checking the default selection");
        var intent = intentOverride ?? "explicit";
        if (intentOverride is null && metadata?.Content?.Entries.TryGetValue(item.Id, out var content) == true
            && EditorialProjection.TryMemberField(content, "Intent", out var origin)
            && StringComparer.InvariantCultureIgnoreCase.Equals(EditorialProjection.Display(origin), "transitive")) intent = "transitive";
        var node = new GraphNode(item.Id, version?.Id ?? item.VersionId ?? "", item.Name, Kind(item.Kind), item.Side,
            facts?.DefaultEnabled ?? true, mods, requirements.ToImmutable(), warnings.ToImmutable());
        budget.Node(node);
        return new(item, node, version, selected, facts?.Pinned ?? false, intent);
    }

    private static string WarningText(DomainException error)
    {
        // Legacy node warnings contain the diagnostic, not merely its headline.
        // Keep details and actionable hint without depending on a human renderer.
        var lines = new List<string> { Sentence(error.Message) };
        if (!string.IsNullOrWhiteSpace(error.Details)) lines.Add("Details: " + Sentence(error.Details));
        if (!string.IsNullOrWhiteSpace(error.Hint)) lines.Add("Try: " + error.Hint.Trim());
        return string.Join(Environment.NewLine, lines);
        static string Sentence(string value)
        { var text = value.Trim(); return text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') ? text : text + "."; }
    }
}
