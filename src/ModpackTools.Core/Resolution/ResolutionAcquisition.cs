using System.Collections.Immutable;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;

namespace ModpackTools.Core.Resolution;

public sealed record ResolutionAcquisitionLimits(ProjectHealthLimits? Observation = null, int MaximumDomains = 10000, int MaximumNodes = 10000);

public sealed class ResolutionAcquisition : IResolutionCandidates, IAsyncDisposable
{
    private readonly ProjectContext project;
    private readonly string configurationDirectory;
    private readonly DateTimeOffset now;
    private readonly ResolutionAcquisitionLimits limits;
    private readonly ProjectHealthLimits observationLimits;
    private readonly GraphReportLimits graphLimits;
    private readonly ArtifactTraversalLimits traversal;
    private readonly ProviderModelBudget models;
    private readonly ContentNodeBudget budget;
    private readonly CancellationTokenSource lifetime;
    private readonly ModrinthVersionClient provider;
    private readonly ArtifactProvider artifacts;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, ProjectContentNode> before = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProviderCandidateObservation> domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProviderProject> identified = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, ProjectContentNode>> materialized = new(StringComparer.OrdinalIgnoreCase);
    private int nodeCount;
    private bool disposed;
    public ProjectHealthObservation? InitialObservation { get; private set; }
    public ImmutableArray<ProjectContentNode> Before { get; private set; } = [];
    public ImmutableArray<ProviderAcquisitionDiagnostic> Diagnostics => [.. provider.Diagnostics, .. artifacts.Diagnostics];
    public ProviderRequestStatistics ProviderStatistics => provider.Statistics;
    public ArtifactProviderStatistics ArtifactStatistics => artifacts.Statistics;

    private ResolutionAcquisition(ProjectContext project, string configurationDirectory, DateTimeOffset now,
        ResolutionAcquisitionLimits? limits, CancellationToken cancellation, HttpMessageHandler? providerHandler,
        HttpMessageHandler? artifactHandler, Uri? providerBaseUri)
    {
        ArgumentNullException.ThrowIfNull(project);
        this.project = project; this.configurationDirectory = Path.GetFullPath(configurationDirectory); this.now = now;
        this.limits = limits ?? new();
        if (this.limits.MaximumDomains <= 0 || this.limits.MaximumNodes <= 0)
            throw new DomainException("Compatibility.InvalidAcquisitionPolicy", "Candidate acquisition limits must be positive", "use positive acquisition quotas");
        observationLimits = this.limits.Observation ?? new(); graphLimits = observationLimits.Graph ?? new();
        GraphReportBuilder.ValidatePolicy(graphLimits); ProviderVersionCache.Validate(observationLimits.Cache ?? new());
        traversal = ArtifactArchiveReader.ValidatePolicy(observationLimits.Traversal);
        models = new(observationLimits.Models); budget = new(graphLimits);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try { provider = new(this.configurationDirectory, now, observationLimits.Provider, lifetime.Token, providerHandler, providerBaseUri, observationLimits.Models, retainCandidateEvidence: true); }
        catch { lifetime.Dispose(); gate.Dispose(); throw; }
        try { artifacts = new(this.configurationDirectory, observationLimits.Artifacts, lifetime.Token, artifactHandler); }
        catch { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); lifetime.Dispose(); gate.Dispose(); throw; }
    }

    public static async Task<ResolutionAcquisition> CreateAsync(ProjectContext project, string configurationDirectory, DateTimeOffset now,
        ResolutionAcquisitionLimits? limits = null, CancellationToken cancellation = default, HttpMessageHandler? providerHandler = null,
        HttpMessageHandler? artifactHandler = null, Uri? providerBaseUri = null)
    {
        var session = new ResolutionAcquisition(project, configurationDirectory, now, limits, cancellation, providerHandler, artifactHandler, providerBaseUri);
        try
        {
            session.InitialObservation = await ProjectHealth.ReadWithSourcesAsync(project, session.configurationDirectory, now, true,
                session.observationLimits, session.lifetime.Token, session.provider, session.artifacts, session.models, session.budget,
                id => session.Domain(id).AsTask()).ConfigureAwait(false);
            session.Initialize(session.InitialObservation.Nodes, charge: false);
            if (session.InitialObservation.ProviderAvailability.Count > session.limits.MaximumDomains) throw Limit(nameof(session.limits.MaximumDomains));
            foreach (var pair in session.InitialObservation.ProviderAvailability) session.domains.TryAdd(pair.Key, pair.Value);
            session.AssertCleanup(); return session;
        }
        catch (OperationCanceledException) { await session.DisposeAsync().ConfigureAwait(false); throw Cancelled(); }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public static async Task<ResolutionAcquisition> FromObservedNodesAsync(ProjectContext project, ImmutableArray<ProjectContentNode> nodes,
        string configurationDirectory, DateTimeOffset now, ResolutionAcquisitionLimits? limits = null, CancellationToken cancellation = default,
        HttpMessageHandler? providerHandler = null, HttpMessageHandler? artifactHandler = null, Uri? providerBaseUri = null)
    {
        var session = new ResolutionAcquisition(project, configurationDirectory, now, limits, cancellation, providerHandler, artifactHandler, providerBaseUri);
        try { session.Initialize(nodes, charge: true); return session; }
        catch (OperationCanceledException) { await session.DisposeAsync().ConfigureAwait(false); throw Cancelled(); }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private void Initialize(ImmutableArray<ProjectContentNode> nodes, bool charge)
    {
        lifetime.Token.ThrowIfCancellationRequested();
        nodes = nodes.IsDefault ? [] : nodes;
        GraphReportBuilder.ValidateInput(new(project.Minecraft ?? "", project.Loader ?? "", project.LoaderVersion ?? "", project.JavaDeclared, project.JavaVersion),
            [.. nodes.Select(node => node.Graph)], graphLimits);
        foreach (var node in nodes)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(node.Item.Id, node.Graph.Id))
                throw new DomainException("Compatibility.CandidateIdentityMismatch", "Observed item and graph identities differ", "supply a coherent owned observation", "InvalidData", node.Graph.Id);
            if (!before.TryAdd(node.Graph.Id, node)) throw new DomainException("Content.DuplicateIdentity", "Duplicate content ID '" + node.Graph.Id + "'", "supply one observed node per identity", "InvalidData", node.Graph.Id);
            if (!charge) continue;
            foreach (var mod in Items(node.Graph.Mods)) budget.Mod(mod);
            foreach (var warning in Items(node.Graph.Warnings)) budget.Warning(warning);
            foreach (var requirement in Items(node.Graph.Requirements)) budget.CountRequirement(requirement);
            budget.Node(node.Graph);
        }
        Before = nodes;
    }

    public async ValueTask<ResolutionDomain> GetCandidatesAsync(string id, CancellationToken cancellation)
    {
        var observed = await ObserveCandidatesAsync(id, cancellation).ConfigureAwait(false);
        return new(observed.Availability, [.. observed.Versions.Select(version => version.Id)]);
    }

    public async ValueTask<ProviderCandidateObservation> ObserveCandidatesAsync(string id, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var registration = cancellation.Register(lifetime.Cancel);
        var acquired = false;
        try
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false); acquired = true;
            var observed = await Domain(id).ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            return observed;
        }
        catch (OperationCanceledException) { throw Cancelled(); }
        finally { if (acquired) gate.Release(); }
    }

    private async ValueTask<ProviderCandidateObservation> Domain(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this); lifetime.Token.ThrowIfCancellationRequested();
        if (domains.TryGetValue(id, out var existing)) return existing;
        if (domains.Count >= limits.MaximumDomains) throw Limit(nameof(limits.MaximumDomains));
        if (!id.StartsWith("modrinth:", StringComparison.Ordinal)) throw new DomainException("Versions.UnsupportedProvider", "Candidate acquisition requires Modrinth content", "select Modrinth-managed content", "NotImplemented", id);
        identified.TryGetValue(id, out var supplied);
        var value = await ProviderCandidates.ObserveAsync(configurationDirectory, id[9..], project.Minecraft ?? "", project.Loader ?? "", now, provider, supplied).ConfigureAwait(false);
        AssertCleanup(); domains.Add(id, value); return value;
    }

    public async ValueTask<ProviderProject> LookupProjectAsync(string selector, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var registration = cancellation.Register(lifetime.Cancel);
        var acquired = false;
        try
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false); acquired = true;
            ObjectDisposedException.ThrowIf(disposed, this); lifetime.Token.ThrowIfCancellationRequested();
            var info = await provider.LookupProjectAsync(selector).ConfigureAwait(false);
            AssertCleanup(); lifetime.Token.ThrowIfCancellationRequested();
            // First owned evidence wins within an invocation; aliases must not
            // replace a project already used to construct a candidate domain.
            identified.TryAdd("modrinth:" + info.Id, info);
            return info;
        }
        catch (OperationCanceledException) { throw Cancelled(); }
        finally { if (acquired) gate.Release(); }
    }

    public async ValueTask<ResolutionNode> GetNodeAsync(string id, string versionId, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var registration = cancellation.Register(lifetime.Cancel);
        var acquired = false;
        try
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false); acquired = true;
            ObjectDisposedException.ThrowIf(disposed, this); lifetime.Token.ThrowIfCancellationRequested();
            if (materialized.TryGetValue(id, out var cached) && cached.TryGetValue(versionId, out var existing)) return ToResolutionNode(existing);
            if (nodeCount >= limits.MaximumNodes) throw Limit(nameof(limits.MaximumNodes));
            var observed = await Domain(id).ConfigureAwait(false);
            var version = observed.Versions.FirstOrDefault(value => value.Id.Equals(versionId, StringComparison.Ordinal))
                ?? throw new DomainException("Versions.NotFound", "Candidate version is not in the acquired compatible domain", "choose an acquired compatible version", "ObjectNotFound", versionId);
            var info = observed.Project ?? throw new DomainException("Metadata.CandidateEvidenceUnavailable", "Candidate project metadata is unavailable", "refresh candidate metadata", "InvalidData", id);
            InventoryItem item; string intent;
            if (before.TryGetValue(id, out var installed)) { item = installed.Item; intent = installed.Intent; }
            else
            {
                var kind = info.Kind switch { ProviderContentKind.Mod => ContentKind.Mod, ProviderContentKind.ResourcePack => ContentKind.ResourcePack, _ => ContentKind.ShaderPack };
                var side = StringComparer.InvariantCultureIgnoreCase.Equals(info.ServerSide, "unsupported") ? "client"
                    : StringComparer.InvariantCultureIgnoreCase.Equals(info.ClientSide, "unsupported") ? "server" : "both";
                item = new(id, kind, info.Title, "", side, ContentSource.Packwiz, "", null, false, "unclassified"); intent = "transitive";
            }
            var node = await ContentNodeReader.ReadAsync(project, item, version, true, artifacts,
                async value => await provider.GetVersionAsync(value).ConfigureAwait(false), graphLimits, traversal, budget, [], intentOverride: intent).ConfigureAwait(false);
            if (node.File is null) throw new DomainException("Versions.MissingFile", $"Version '{version.Id}' has no installable file", "choose another version", "InvalidData");
            AssertCleanup(); lifetime.Token.ThrowIfCancellationRequested();
            provider.StoreCandidateVersion(info, project.Minecraft ?? "", project.Loader ?? "", version.Id);
            AssertCleanup(); lifetime.Token.ThrowIfCancellationRequested();
            cached ??= new(StringComparer.Ordinal); cached.Add(versionId, node); materialized[id] = cached; nodeCount++;
            return ToResolutionNode(node);
        }
        catch (OperationCanceledException) { throw Cancelled(); }
        finally { if (acquired) gate.Release(); }
    }

    public ProjectContentNode GetMaterializedNode(string id, string versionId) => materialized.TryGetValue(id, out var values) && values.TryGetValue(versionId, out var node)
        ? node : throw new InvalidOperationException("The selected node must be acquired before preparing an installation plan.");

    internal ProjectContentNode ExportSelectedNode(ResolutionNode selected)
    {
        var id = selected.Graph.Id;
        if (materialized.TryGetValue(id, out var versions) && versions.TryGetValue(selected.Graph.VersionId, out var candidate) && ReferenceEquals(candidate.Graph, selected.Graph)) return candidate;
        if (before.TryGetValue(id, out var observed) && ReferenceEquals(observed.Graph, selected.Graph)) return observed;
        throw new InvalidOperationException("A selected node must belong to this acquisition invocation.");
    }

    public static ResolutionNode ToResolutionNode(ProjectContentNode node) => new(node.Graph, node.Pinned, node.Item.Source == ContentSource.Local,
        StringComparer.InvariantCultureIgnoreCase.Equals(node.Intent, "explicit"), OrderingDate(node.Version),
        node.Version?.Loaders.Any(loader => StringComparer.InvariantCultureIgnoreCase.Equals(loader, "forge") || StringComparer.InvariantCultureIgnoreCase.Equals(loader, "neoforge")) == true);

    private static DateTime? OrderingDate(ProviderVersion? version) => version is null || version.Published.Value is null && version.Published.Text.Length == 0
        ? null : ProviderCandidates.PublicationDate(version);

    private void AssertCleanup()
    {
        if (Diagnostics.FirstOrDefault() is { } error) throw new DomainException("Metadata.ProviderCleanupFailed", "Acquisition temporary file could not be removed",
            "check private cache permissions and file locks", "WriteError", error.Path, details: error.Message);
    }
    private static DomainException Cancelled() => new("Core.OperationCancelled", "Candidate acquisition was cancelled", "retry when ready", "OperationStopped");
    private static DomainException Limit(string name) => new("Compatibility.AcquisitionLimit", "Candidate acquisition exceeds its resource limits", "use bounded candidate acquisition quotas", "LimitsExceeded", name);
    private static ImmutableArray<T> Items<T>(ImmutableArray<T> values) => values.IsDefault ? [] : values;
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; lifetime.Cancel();
        await gate.WaitAsync().ConfigureAwait(false);
        try { await provider.DisposeAsync().ConfigureAwait(false); }
        finally { try { await artifacts.DisposeAsync().ConfigureAwait(false); } finally { lifetime.Dispose(); gate.Release(); gate.Dispose(); } }
    }
}
