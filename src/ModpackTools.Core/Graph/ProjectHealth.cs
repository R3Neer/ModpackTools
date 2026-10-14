using System.Collections.Immutable;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Graph;

public sealed record ProjectHealthLimits(InventoryReadLimits? Inventory = null, GraphReportLimits? Graph = null,
    ProviderCacheReadLimits? Cache = null, ProviderRequestLimits? Provider = null,
    ArtifactProviderLimits? Artifacts = null, ArtifactTraversalLimits? Traversal = null, ProviderModelLimits? Models = null);
public sealed record ProjectContentNode(InventoryItem Item, GraphNode Graph, ProviderVersion? Version,
    ProviderFile? File, bool Pinned, string Intent);
public sealed record ProjectHealthObservation(RawInventorySnapshot Inventory, ImmutableArray<ProjectContentNode> Nodes,
    GraphReport Report, ImmutableDictionary<string, ProviderCandidateObservation> ProviderAvailability);

public static class ProjectHealth
{
    public static async Task<ProjectHealthObservation> ReadAsync(ProjectContext project, string configurationDirectory,
        DateTimeOffset now, bool check = false, ProjectHealthLimits? limits = null, CancellationToken cancellation = default,
        HttpMessageHandler? providerHandler = null, HttpMessageHandler? artifactHandler = null, Uri? providerBaseUri = null)
    {
        try { return await ReadCore(project, configurationDirectory, now, check, limits, cancellation, providerHandler, artifactHandler, providerBaseUri).ConfigureAwait(false); }
        catch (OperationCanceledException)
        { throw new DomainException("Core.OperationCancelled", "Project health observation was cancelled", "retry when ready", "OperationStopped"); }
    }

    private static async Task<ProjectHealthObservation> ReadCore(ProjectContext project, string configurationDirectory,
        DateTimeOffset now, bool check, ProjectHealthLimits? limits, CancellationToken cancellation,
        HttpMessageHandler? providerHandler, HttpMessageHandler? artifactHandler, Uri? providerBaseUri)
    {
        ArgumentNullException.ThrowIfNull(project); limits ??= new();
        var graphPolicy = limits.Graph ?? new(); GraphReportBuilder.ValidatePolicy(graphPolicy);
        var cachePolicy = limits.Cache ?? new(); ProviderVersionCache.Validate(cachePolicy);
        var models = new ProviderModelBudget(limits.Models);
        var traversal = ArtifactArchiveReader.ValidatePolicy(limits.Traversal);
        await using var provider = new ModrinthVersionClient(configurationDirectory, now, limits.Provider, cancellation, providerHandler, providerBaseUri, limits.Models);
        await using var artifacts = new ArtifactProvider(configurationDirectory, limits.Artifacts, cancellation, artifactHandler);
        cancellation.ThrowIfCancellationRequested();
        var inventory = InventoryReader.ReadForHealth(project, limits.Inventory);
        var count = (long)inventory.Mods.Length + inventory.Resources.Length + inventory.Shaders.Length;
        GraphResource.Guard(count > graphPolicy.MaximumNodes, nameof(graphPolicy.MaximumNodes));
        var items = inventory.Mods.Concat(inventory.Resources).Concat(inventory.Shaders).ToArray();
        var identities = new Dictionary<string, InventoryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            if (!identities.TryAdd(item.Id, item)) throw new DomainException("Content.DuplicateIdentity", $"Duplicate content ID '{item.Id}'",
                "resolve the duplicate metadata before changing or building the pack", "InvalidData", item.Id,
                details: $"Metadata files: '{identities[item.Id].MetadataPath}' and '{item.MetadataPath}'");
        var installed = new Dictionary<string, ProviderVersion>(StringComparer.OrdinalIgnoreCase);
        string? batchWarning = null;
        if (check)
        {
            try
            {
                foreach (var version in await provider.GetVersionsAsync(items.Where(IsProvider).Select(item => item.VersionId)).ConfigureAwait(false))
                    installed[version.Id] = version;
            }
            catch (DomainException error) when (Recoverable(error)) { batchWarning = error.Message; }
        }
        var versionObservations = new Dictionary<string, ProviderVersion?>(StringComparer.Ordinal);
        var versionFailures = new Dictionary<string, DomainException>(StringComparer.Ordinal);
        var nodes = ImmutableArray.CreateBuilder<ProjectContentNode>(items.Length);
        var budget = new HealthBudget(graphPolicy);
        foreach (var item in items)
        {
            cancellation.ThrowIfCancellationRequested();
            var warnings = ImmutableArray.CreateBuilder<string>();
            void Warn(string value) { budget.Warning(value); warnings.Add(value); }
            ProviderVersion? version = null;
            if (IsProvider(item))
            {
                try
                {
                    version = check ? installed.GetValueOrDefault(item.VersionId!) : await Version(item.VersionId!).ConfigureAwait(false);
                    if (check && version is null) Warn(batchWarning ?? "Installed provider version metadata is unavailable");
                    if (version is not null && version.ProjectId != item.Id[9..])
                    { version = null; Warn("Installed provider version metadata has a different identity"); }
                }
                catch (DomainException error) when (Recoverable(error)) { Warn(error.Message); }
            }
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
                        try { id = (await Version(dependency.VersionId).ConfigureAwait(false))?.ProjectId ?? ""; }
                        catch (DomainException error) when (Recoverable(error)) { Warn(error.Message); }
                    }
                    if (id.Length == 0) { Warn("A provider dependency has no verifiable project identity"); continue; }
                    budget.Requirement();
                    requirements.Add(new("modrinth:" + id, VersionConstraint.Text("*"), kind, "both", "project",
                        VersionScheme.General, "provider", dependency.VersionId, [], [], null));
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
                catch (DomainException error) when (Recoverable(error)) { Warn(error.Message); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Warn(error.Message); }
            }
            var facts = item.InstallFacts;
            if (facts?.Optional == true) Warn("Optional install combinations require separate verification; checking the default selection");
            var intent = "explicit";
            if (inventory.Metadata.Content?.Entries.TryGetValue(item.Id, out var content) == true
                && EditorialProjection.TryMemberField(content, "Intent", out var origin)
                && StringComparer.InvariantCultureIgnoreCase.Equals(EditorialProjection.Display(origin), "transitive")) intent = "transitive";
            var node = new GraphNode(item.Id, version?.Id ?? item.VersionId ?? "", item.Name, Kind(item.Kind), item.Side,
                facts?.DefaultEnabled ?? true, mods, requirements.ToImmutable(), warnings.ToImmutable());
            budget.Node(node);
            nodes.Add(new(item, node, version, selected, facts?.Pinned ?? false, intent));
        }
        var environment = new GraphEnvironment(project.Minecraft ?? "", project.Loader ?? "", project.LoaderVersion ?? "", project.JavaDeclared, project.JavaVersion);
        var graphNodes = nodes.Select(node => node.Graph).ToImmutableArray();
        var overrides = new RuntimeOverrides(Exists("fabric_loader_dependencies.json"), Exists("quilt-loader-overrides.json"));
        var report = GraphReportBuilder.Build(environment, graphNodes, overrides, graphPolicy);
        var availability = ImmutableDictionary.CreateBuilder<string, ProviderCandidateObservation>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in report.Errors)
        {
            cancellation.ThrowIfCancellationRequested();
            var requirement = issue.Requirement;
            if (requirement is not { Scope: "project", Source: "provider" } || identities.ContainsKey(requirement.Target)
                || !requirement.Target.StartsWith("modrinth:", StringComparison.Ordinal) || availability.ContainsKey(requirement.Target)) continue;
            availability[requirement.Target] = await ProviderCandidates.ObserveWithBudget(configurationDirectory, requirement.Target[9..],
                project.Minecraft ?? "", project.Loader ?? "", now, check ? provider : null, check ? null : cachePolicy, models).ConfigureAwait(false);
        }
        const string suffix = "; provider metadata exposes no compatible candidate";
        long messageCharacters = report.Issues.Sum(issue => (long)issue.Message.Length);
        var reconciled = ImmutableArray.CreateBuilder<GraphIssue>(report.Issues.Length);
        foreach (var issue in report.Issues)
        {
            var replacement = issue;
            if (issue.Severity == "error" && issue.Requirement is { Scope: "project", Source: "provider" } requirement
                && !identities.ContainsKey(requirement.Target) && availability.TryGetValue(requirement.Target, out var observed)
                && observed.Availability == CandidateAvailability.KnownEmpty)
            {
                GraphResource.Guard(suffix.Length > graphPolicy.MaximumMessageCharacters - messageCharacters, nameof(graphPolicy.MaximumMessageCharacters));
                messageCharacters += suffix.Length;
                replacement = issue.Reclassify("provider.no-compatible-candidate", "unknown", issue.Message + suffix);
            }
            reconciled.Add(replacement);
        }
        cancellation.ThrowIfCancellationRequested();
        return new(inventory, nodes.ToImmutable(), new GraphReport(reconciled.ToImmutable()), availability.ToImmutable());

        async Task<ProviderVersion?> Version(string id)
        {
            if (versionObservations.TryGetValue(id, out var existing)) return existing;
            if (versionFailures.TryGetValue(id, out var failure)) throw failure;
            try
            {
                ProviderVersion? value;
                if (check) value = await provider.GetVersionAsync(id).ConfigureAwait(false);
                else
                {
                    var observed = ProviderVersionCache.ReadWithBudget(configurationDirectory, id, now, cachePolicy, models);
                    if (observed.Status == ProviderCacheStatus.Unreadable) throw new DomainException("Metadata.InvalidProviderResponse",
                        "Installed provider cache is unreadable", "refresh or inspect provider metadata", "InvalidData", observed.Path, details: observed.Diagnostic);
                    value = observed.Version;
                }
                versionObservations.Add(id, value); return value;
            }
            catch (DomainException error) when (Recoverable(error)) { versionFailures.Add(id, error); throw; }
        }
        bool Exists(string name)
        { var path = Path.Combine(project.Root, "config", name); return File.Exists(path) || Directory.Exists(path); }
    }

    private static bool IsProvider(InventoryItem item) => item.Id.StartsWith("modrinth:", StringComparison.Ordinal) && !string.IsNullOrEmpty(item.VersionId);
    private static bool Recoverable(DomainException error) => error.Category is not ("LimitsExceeded" or "OperationStopped");
    private static string Kind(ContentKind kind) => kind switch { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", _ => "shaderpack" };
    private static string Contained(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Transaction.UnsafePath", $"Path '{relative}' escapes the project", "repair the referenced path", "InvalidData");
        return path;
    }
    private sealed class HealthBudget(GraphReportLimits limits)
    {
        public int Mods { get; private set; }
        public int Requirements { get; private set; }
        public int Aliases { get; private set; }
        public int Warnings => warnings;
        private int warnings;
        private long characters;
        public void Warning(string value) { GraphResource.Guard(warnings++ >= limits.MaximumWarnings, nameof(limits.MaximumWarnings)); Text(value); }
        public void Requirement()
        { GraphResource.Guard(Requirements >= limits.MaximumRequirements, nameof(limits.MaximumRequirements)); Requirements++; }
        public void Mod(ArtifactMod mod)
        {
            GraphResource.Guard(Mods >= limits.MaximumMods, nameof(limits.MaximumMods)); Mods++;
            Text(mod.Declaration.Id); Text(mod.Declaration.Version);
            foreach (var alias in mod.Declaration.Provides)
            { GraphResource.Guard(Aliases >= limits.MaximumAliases, nameof(limits.MaximumAliases)); Aliases++; Text(alias.Version); }
            foreach (var requirement in mod.Declaration.Requirements) CountRequirement(requirement);
        }
        public void Node(GraphNode node) { Text(node.Id); Text(node.Name); Text(node.VersionId); }
        private void CountRequirement(ManifestRequirement requirement)
        {
            Requirement(); Text(requirement.Target);
            foreach (var child in requirement.Any) CountRequirement(child);
            foreach (var child in requirement.All) CountRequirement(child);
            if (requirement.Unless is not null) CountRequirement(requirement.Unless);
        }
        private void Text(string value) { GraphResource.Guard(value.Length > limits.MaximumTextCharacters - characters, nameof(limits.MaximumTextCharacters)); characters += value.Length; }
    }
}
