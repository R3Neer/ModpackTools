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
    GraphReport Report, ImmutableDictionary<string, ProviderCandidateObservation> ProviderAvailability)
{
    public RuntimeOverrides RuntimeOverrides { get; init; } = new();
}

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
        _ = ArtifactArchiveReader.ValidatePolicy(limits.Traversal);
        await using var provider = new ModrinthVersionClient(configurationDirectory, now, limits.Provider, cancellation, providerHandler, providerBaseUri, limits.Models);
        await using var artifacts = new ArtifactProvider(configurationDirectory, limits.Artifacts, cancellation, artifactHandler);
        return await ReadWithSourcesAsync(project, configurationDirectory, now, check, limits, cancellation,
            provider, artifacts, models, new(graphPolicy)).ConfigureAwait(false);
    }

    internal static async Task<ProjectHealthObservation> ReadWithSourcesAsync(ProjectContext project, string configurationDirectory,
        DateTimeOffset now, bool check, ProjectHealthLimits limits, CancellationToken cancellation,
        ModrinthVersionClient provider, ArtifactProvider artifacts, ProviderModelBudget models, ContentNodeBudget budget,
        Func<string, Task<ProviderCandidateObservation>>? readAvailability = null)
    {
        var graphPolicy = limits.Graph ?? new(); GraphReportBuilder.ValidatePolicy(graphPolicy);
        var cachePolicy = limits.Cache ?? new(); ProviderVersionCache.Validate(cachePolicy);
        var traversal = ArtifactArchiveReader.ValidatePolicy(limits.Traversal);
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
            nodes.Add(await ContentNodeReader.ReadAsync(project, item, version, check, artifacts, Version,
                graphPolicy, traversal, budget, warnings.ToImmutable(), inventory.Metadata).ConfigureAwait(false));
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
            availability[requirement.Target] = readAvailability is not null
                ? await readAvailability(requirement.Target).ConfigureAwait(false)
                : await ProviderCandidates.ObserveWithBudget(configurationDirectory, requirement.Target[9..],
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
        return new(inventory, nodes.ToImmutable(), new GraphReport(reconciled.ToImmutable()), availability.ToImmutable()) { RuntimeOverrides = overrides };

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
    internal static bool Recoverable(DomainException error) => error.Category is not ("LimitsExceeded" or "OperationStopped");
    internal static string Kind(ContentKind kind) => kind switch { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", _ => "shaderpack" };
    internal static string Contained(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Transaction.UnsafePath", $"Path '{relative}' escapes the project", "repair the referenced path", "InvalidData");
        return path;
    }
}
