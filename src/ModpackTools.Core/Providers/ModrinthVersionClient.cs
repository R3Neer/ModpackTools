using System.Collections.Immutable;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModpackTools.Core.Data;
using ModpackTools.Core.Source;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Providers;

public sealed record ProviderRequestLimits(int MaximumRequests = 1000, int MaximumConcurrency = 4,
    int TimeoutMilliseconds = 60000, long MaximumResponseBytes = 16 * 1024 * 1024,
    long MaximumTotalResponseBytes = 64 * 1024 * 1024, int MaximumInputIds = 10000,
    int MaximumEndpointCharacters = 32768, int MaximumTotalEndpointCharacters = 1024 * 1024,
    int MaximumReturnedVersions = 10000, ProviderCacheReadLimits? Decode = null,
    long MaximumListingWireBytes = 32 * 1024 * 1024, int MaximumSearchResults = 10000);
public sealed record ProviderRequestStatistics(int Requests, long Bytes);
public sealed record ProviderAcquisitionDiagnostic(string Code, string Path, string Message);

public sealed class ModrinthVersionClient : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate;
    private readonly CancellationTokenSource lifetime;
    private readonly ProviderRequestLimits limits;
    private readonly ProviderCacheReadLimits decode;
    private readonly ProviderModelBudget models;
    private readonly string directory;
    private readonly DateTimeOffset now;
    private readonly StringComparer comparer;
    private readonly bool retainCandidateEvidence;
    private readonly object sync = new();
    private enum ResponseKind { Version, Batch, Project, Candidates, PublicVersions, Search }
    private sealed record ProviderResponse(ProviderProject? Project, ImmutableArray<ProviderVersion> Versions)
    {
        public ImmutableArray<CompatibleVersion> Listings { get; init; } = [];
        public SearchResults? Search { get; init; }
        public ImmutableDictionary<string, string> CandidateWires { get; init; } = ImmutableDictionary<string, string>.Empty;
    }
    private readonly Dictionary<string, Lazy<Task<ProviderResponse>>> pending = new(StringComparer.Ordinal);
    private readonly List<ProviderAcquisitionDiagnostic> diagnostics = [];
    private long bytes, endpointCharacters, listingWireBytes;
    private int requests, versions, inputIds;
    private bool disposed;
    private DomainException? exhausted;

    public ModrinthVersionClient(string configurationDirectory, DateTimeOffset now, ProviderRequestLimits? limits = null,
        CancellationToken cancellation = default, HttpMessageHandler? handler = null, Uri? baseUri = null, ProviderModelLimits? modelLimits = null,
        bool retainCandidateEvidence = false)
    {
        this.limits = limits ?? new(); this.now = now;
        this.retainCandidateEvidence = retainCandidateEvidence;
        var invalid = this.limits.MaximumRequests <= 0 ? nameof(this.limits.MaximumRequests)
            : this.limits.MaximumConcurrency is < 1 or > 64 ? nameof(this.limits.MaximumConcurrency)
            : this.limits.TimeoutMilliseconds <= 0 ? nameof(this.limits.TimeoutMilliseconds)
            : this.limits.MaximumResponseBytes <= 0 ? nameof(this.limits.MaximumResponseBytes)
            : this.limits.MaximumTotalResponseBytes <= 0 ? nameof(this.limits.MaximumTotalResponseBytes)
            : this.limits.MaximumInputIds <= 0 ? nameof(this.limits.MaximumInputIds)
            : this.limits.MaximumEndpointCharacters <= 0 ? nameof(this.limits.MaximumEndpointCharacters)
            : this.limits.MaximumTotalEndpointCharacters <= 0 ? nameof(this.limits.MaximumTotalEndpointCharacters)
            : this.limits.MaximumReturnedVersions <= 0 ? nameof(this.limits.MaximumReturnedVersions)
            : this.limits.MaximumListingWireBytes <= 0 ? nameof(this.limits.MaximumListingWireBytes)
            : this.limits.MaximumSearchResults <= 0 ? nameof(this.limits.MaximumSearchResults) : null;
        if (invalid is not null) throw new DomainException("Metadata.InvalidProviderRequestPolicy", "Provider request limits are invalid",
            "use positive quotas and concurrency between 1 and 64", target: invalid);
        decode = this.limits.Decode ?? new(); ProviderVersionCache.Validate(decode);
        models = new(modelLimits);
        directory = Path.Combine(Path.GetFullPath(configurationDirectory), "metadata");
        baseUri ??= new("https://api.modrinth.com/v2/");
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("http" or "https") || baseUri.UserInfo.Length != 0
            || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0 || !baseUri.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("Provider base URI must be an absolute HTTP(S) directory without credentials, query or fragment.", nameof(baseUri));
        comparer = StringComparer.CurrentCultureIgnoreCase;
        http = new(handler ?? new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = this.limits.MaximumConcurrency
        }, disposeHandler: true)
        { BaseAddress = baseUri, Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("R3Neer-ModpackTools/" + ProductVersion.Current.Version);
        gate = new(this.limits.MaximumConcurrency, this.limits.MaximumConcurrency);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
    }

    public ProviderRequestStatistics Statistics { get { lock (sync) return new(requests, bytes); } }
    public ImmutableArray<ProviderAcquisitionDiagnostic> Diagnostics { get { lock (sync) return [.. diagnostics]; } }

    public async Task<ProviderVersion> GetVersionAsync(string id)
    {
        Input();
        ValidateId(id);
        var result = await Request("version/" + id, [id], ResponseKind.Version).ConfigureAwait(false);
        return result.Versions[0];
    }

    public Task<ImmutableArray<ProviderVersion>> GetVersionsAsync(IEnumerable<string?> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var unique = new SortedSet<string>(comparer);
        foreach (var id in ids)
        {
            Input();
            if (string.IsNullOrEmpty(id)) continue;
            ValidateId(id); unique.Add(id);
        }
        var selected = unique.ToArray();
        if (selected.Length == 0)
        {
            lock (sync) { ObjectDisposedException.ThrowIf(disposed, this); CheckBudget(); }
            if (lifetime.IsCancellationRequested) throw new DomainException("Core.OperationCancelled", "Provider lookup was cancelled", "retry when ready", "OperationStopped");
            return Task.FromResult(ImmutableArray<ProviderVersion>.Empty);
        }
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        { writer.WriteStartArray(); foreach (var id in selected) writer.WriteStringValue(id); writer.WriteEndArray(); }
        var json = System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return Batch();
        async Task<ImmutableArray<ProviderVersion>> Batch() =>
            (await Request("versions?ids=" + Uri.EscapeDataString(json), selected, ResponseKind.Batch).ConfigureAwait(false)).Versions;
    }

    public async Task<ProviderProject> GetProjectAsync(string id)
    {
        Input(); ProviderProject.ValidateId(id);
        return (await Request("project/" + id, [id], ResponseKind.Project).ConfigureAwait(false)).Project!;
    }

    public async Task<ImmutableArray<ProviderVersion>> GetCandidateVersionsAsync(ProviderProject project,
        string minecraftVersion, string loader)
    {
        ArgumentNullException.ThrowIfNull(project); Input();
        return (await Request(project.CandidateEndpoint(minecraftVersion, loader), [project.Id], ResponseKind.Candidates).ConfigureAwait(false)).Versions;
    }

    public async Task<ImmutableArray<CompatibleVersion>> GetCompatibleVersionsAsync(string projectId, ContentKind kind,
        string minecraftVersion, string loader)
    {
        Input();
        return (await Request(CompatibleVersion.Endpoint(projectId, kind, minecraftVersion, loader), [projectId], ResponseKind.PublicVersions).ConfigureAwait(false)).Listings;
    }

    public async Task<SearchResults> SearchAsync(string query, string type, int limit, ProjectContext? project)
    {
        Input();
        return (await Request(ProviderSearch.Endpoint(query, type, limit, project), [], ResponseKind.Search).ConfigureAwait(false)).Search!;
    }

    internal void StoreCandidateVersion(ProviderProject project, string minecraftVersion, string loader, string versionId)
    {
        Input();
        var endpoint = project.CandidateEndpoint(minecraftVersion, loader);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this); CheckBudget();
            if (!retainCandidateEvidence || !pending.TryGetValue(endpoint, out var response) || !response.IsValueCreated || !response.Value.IsCompletedSuccessfully
                || !response.Value.Result.CandidateWires.TryGetValue(versionId, out var wire))
                throw new DomainException("Metadata.CandidateEvidenceUnavailable", "Selected candidate has no owned response evidence",
                    "acquire the candidate through this planning invocation", "InvalidData", versionId);
            // This wire is private, captured from the same already validated row.
            // Revalidation here would silently impose WriteRawValue's depth64.
            try { Store(writer => writer.WriteRawValue(wire, skipInputValidation: true), "version/" + versionId, lifetime.Token); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { throw WriteFailure(exception.Message); }
        }
    }

    private Task<ProviderResponse> Request(string endpoint, string[] ids, ResponseKind kind)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this); CheckBudget();
            if (pending.TryGetValue(endpoint, out var existing)) return existing.Value;
            if (pending.Count >= limits.MaximumRequests) throw Resource(nameof(limits.MaximumRequests));
            if (endpoint.Length > limits.MaximumEndpointCharacters) throw Resource(nameof(limits.MaximumEndpointCharacters));
            if (endpoint.Length > limits.MaximumTotalEndpointCharacters - endpointCharacters) throw Resource(nameof(limits.MaximumTotalEndpointCharacters));
            endpointCharacters += endpoint.Length;
            var work = new Lazy<Task<ProviderResponse>>(() => Fetch(endpoint, ids, kind));
            pending.Add(endpoint, work);
            return work.Value;
        }
    }

    private async Task<ProviderResponse> Fetch(string endpoint, string[] ids, ResponseKind kind)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(limits.TimeoutMilliseconds);
        var token = timeout.Token;
        // Register the task under the lock before executing transport or disk work.
        await Task.Yield();
        var acquired = false;
        var temporary = Path.Combine(directory, ".provider-" + Guid.NewGuid().ToString("N") + ".tmp");
        var owned = false; var receiving = false;
        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false); acquired = true;
            lock (sync) { CheckBudget(); requests++; }
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            GuardPath(directory); Directory.CreateDirectory(directory); GuardPath(directory);
            ProviderResponse result;
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                owned = true;
                using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var buffer = new byte[8192]; long localBytes = 0; int read;
                while (true)
                {
                    int allowance;
                    lock (sync)
                    {
                        CheckBudget();
                        var remaining = Math.Min(limits.MaximumResponseBytes - localBytes, limits.MaximumTotalResponseBytes - bytes);
                        allowance = (int)Math.Min(buffer.Length, remaining >= buffer.Length ? buffer.Length : remaining + 1);
                    }
                    receiving = true;
                    read = await body.ReadAsync(buffer.AsMemory(0, allowance), token).ConfigureAwait(false);
                    receiving = false;
                    if (read == 0) break;
                    lock (sync)
                    {
                        bytes = read > long.MaxValue - bytes ? long.MaxValue : bytes + read;
                        CheckBudget();
                        if (read > limits.MaximumResponseBytes - localBytes) { exhausted = Resource(nameof(limits.MaximumResponseBytes)); throw exhausted; }
                        if (bytes > limits.MaximumTotalResponseBytes) { exhausted = Resource(nameof(limits.MaximumTotalResponseBytes)); throw exhausted; }
                    }
                    localBytes += read;
                    await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                }
                await file.FlushAsync(token).ConfigureAwait(false); file.Position = 0;
                token.ThrowIfCancellationRequested();
                var decoded = ProviderVersionCache.ReadDocument(file, decode, models.Bytes);
                if (decoded is null) throw Invalid("Provider returned an empty response.");
                using var document = decoded.Document;
                var root = document.RootElement;
                JsonDisplayProjection.Validate(root);
                if (kind == ResponseKind.Search)
                {
                    result = new(null, []) { Search = ProviderSearch.Decode(root, decoded.Dates, limits.MaximumSearchResults, models.Entry) };
                    token.ThrowIfCancellationRequested();
                    return result;
                }
                if (kind is ResponseKind.Candidates or ResponseKind.PublicVersions && root.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
                    throw Invalid("Provider candidate response must be an array or null.");
                var batch = kind is ResponseKind.Batch or ResponseKind.Candidates or ResponseKind.PublicVersions;
                var rowCount = root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : root.ValueKind == JsonValueKind.Null && batch ? 0 : 1;
                if (!batch && rowCount != 1) throw Invalid("Provider single response must contain exactly one object.");
                lock (sync)
                {
                    CheckBudget();
                    if (kind != ResponseKind.Project)
                    {
                        if (rowCount > limits.MaximumReturnedVersions - versions) { exhausted = Resource(nameof(limits.MaximumReturnedVersions)); throw exhausted; }
                        versions += rowCount;
                    }
                }
                var rows = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : rowCount == 0 ? [] : new[] { root };
                var allowed = ids.ToHashSet(StringComparer.Ordinal);
                var values = ImmutableArray.CreateBuilder<ProviderVersion>(rows.Length);
                var listings = ImmutableArray.CreateBuilder<CompatibleVersion>(kind == ResponseKind.PublicVersions ? rows.Length : 0);
                var candidateWires = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
                ProviderProject? project = null;
                var collectionEntries = 0;
                foreach (var row in rows)
                {
                    if (kind == ResponseKind.Project)
                    {
                        project = ProviderProject.Decode(row, decoded.Dates, decode.MaximumCollectionEntries, models.Entry);
                        if (!allowed.Contains(project.Id)) throw Invalid("Provider returned a project identity that was not requested.");
                        continue;
                    }
                    models.Version();
                    var version = ProviderVersionCache.Decode(row, decoded.Dates,
                        kind is ResponseKind.Candidates or ResponseKind.PublicVersions ? decode.MaximumCollectionEntries - collectionEntries : decode.MaximumCollectionEntries, models.Entry);
                    if (kind is ResponseKind.Candidates or ResponseKind.PublicVersions)
                    {
                        if (!allowed.Contains(version.ProjectId)) throw Invalid("Provider candidate belongs to another project.");
                        var entries = (long)version.Files.Length + version.Dependencies.Length + version.Loaders.Length + version.GameVersions.Length;
                        if (entries > decode.MaximumCollectionEntries - collectionEntries) throw Resource("MaximumCollectionEntries");
                        collectionEntries += (int)entries;
                    }
                    else if (!allowed.Contains(version.Id)) throw Invalid("Provider returned an identity that was not requested.");
                    values.Add(version);
                    if (kind == ResponseKind.PublicVersions) listings.Add(CompatibleVersion.Capture(version, row, decoded.Dates, CaptureListingWire));
                    if (kind == ResponseKind.Candidates && retainCandidateEvidence)
                    {
                        var wire = CaptureListingWire(row);
                        if (candidateWires.TryGetValue(version.Id, out var previous) && !StringComparer.Ordinal.Equals(previous, wire))
                            throw Invalid("Candidate response contains conflicting evidence for the same version identity.");
                        candidateWires.TryAdd(version.Id, wire);
                    }
                }
                var typedVersions = values.ToImmutable();
                if (kind == ResponseKind.Candidates) typedVersions = ProviderCandidates.Order(typedVersions);
                result = new(project, typedVersions) { Listings = [.. listings.OrderByDescending(value => value.PublicationTicks)], CandidateWires = candidateWires.ToImmutable() };
                token.ThrowIfCancellationRequested();
                // Every row is valid before the first individually atomic store.
                if (kind is ResponseKind.Project or ResponseKind.Candidates)
                    Store(root, endpoint, token);
                else if (kind != ResponseKind.PublicVersions) for (var index = 0; index < rows.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    lock (sync) CheckBudget();
                    Store(rows[index], "version/" + result.Versions[index].Id, token);
                }
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            lock (sync) if (exhausted is not null) throw exhausted;
            throw lifetime.IsCancellationRequested
                ? new DomainException("Core.OperationCancelled", "Provider lookup was cancelled", "retry when ready", "OperationStopped")
                : new DomainException(kind == ResponseKind.Search ? "Search.RequestTimeout" : "Versions.RequestTimeout", "Provider lookup exceeded its deadline", "check the connection or use an explicit bounded timeout", "OperationTimeout");
        }
        catch (HttpRequestException exception)
        { throw new DomainException(kind == ResponseKind.Search ? "Search.RequestFailed" : "Versions.RequestFailed", kind == ResponseKind.Search ? "Modrinth search could not be completed" : kind == ResponseKind.PublicVersions ? "Modrinth version lookup could not be completed" : "Modrinth request could not be completed", "check the network connection and retry", "ConnectionError", details: exception.Message); }
        catch (CacheJsonLimitException exception) { throw Resource("Decode", exception.Message); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.Metadata.ProviderCacheLimit")
        { throw Resource("Decode", exception.Details); }
        catch (Newtonsoft.Json.JsonReaderException exception) when (exception.Message.StartsWith("The reader's MaxDepth of ", StringComparison.Ordinal))
        { throw Resource("Decode", exception.Message); }
        catch (JsonException exception) when (exception.Message.StartsWith("The maximum configured depth of ", StringComparison.Ordinal))
        { throw Resource("Decode", exception.Message); }
        catch (Exception exception) when (exception is JsonException or Newtonsoft.Json.JsonException or FormatException or InvalidOperationException or InvalidCastException or OverflowException)
        { throw Invalid(exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw receiving ? new DomainException(kind == ResponseKind.Search ? "Search.RequestFailed" : "Versions.RequestFailed", "Provider response body could not be read", "check the network connection and retry", "ConnectionError", details: exception.Message) : WriteFailure(exception.Message); }
        finally
        {
            if (owned) Cleanup(temporary);
            if (acquired) gate.Release();
        }
    }

    private string CaptureListingWire(JsonElement value)
    {
        using var bytes = new MemoryStream();
        using (var bounded = new StoreOutput(bytes, long.MaxValue, count =>
        {
            lock (sync)
            {
                if (count > limits.MaximumListingWireBytes - listingWireBytes) throw Resource(nameof(limits.MaximumListingWireBytes));
                listingWireBytes += count;
            }
        }))
        using (var writer = new Utf8JsonWriter(bounded, new() { MaxDepth = decode.MaximumDepth }))
        {
            // PowerShell's @($null) retains one null dependency occurrence.
            if (value.ValueKind == JsonValueKind.Null) { writer.WriteStartArray(); writer.WriteNullValue(); writer.WriteEndArray(); }
            else value.WriteTo(writer);
        }
        return System.Text.Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
    }

    private void Store(JsonElement row, string endpoint, CancellationToken token) => Store(writer => row.WriteTo(writer), endpoint, token);

    private void Store(Action<Utf8JsonWriter> write, string endpoint, CancellationToken token)
    {
        var path = Path.Combine(directory, TextHash.Compute(endpoint) + ".json");
        var temporary = Path.Combine(directory, ".store-" + Guid.NewGuid().ToString("N") + ".tmp");
        var owned = false;
        try
        {
            GuardPath(path, file: true);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                using (var bounded = new StoreOutput(file, decode.MaximumBytes))
                using (var writer = new Utf8JsonWriter(bounded, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = decode.MaximumDepth }))
                { write(writer); writer.Flush(); }
                File.SetLastWriteTimeUtc(file.SafeFileHandle, now.UtcDateTime);
            }
            token.ThrowIfCancellationRequested();
            lock (sync) CheckBudget();
            GuardPath(path, file: true);
            File.Move(temporary, path, overwrite: true); owned = false;
        }
        finally
        {
            if (owned) Cleanup(temporary);
        }
    }

    private void CheckBudget() { if (exhausted is not null) throw exhausted; }
    private sealed class StoreOutput(Stream destination, long maximumBytes, Action<int>? charge = null) : Stream
    {
        private long written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written;
        public override long Position { get => written; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > maximumBytes - written) throw Resource("CacheBytes");
            charge?.Invoke(buffer.Length);
            destination.Write(buffer); written += buffer.Length;
        }
        public override void Flush() => destination.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private void Input()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this); CheckBudget();
            if (inputIds >= limits.MaximumInputIds) throw Resource(nameof(limits.MaximumInputIds));
            inputIds++;
        }
    }
    private static void ValidateId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length is < 1 or > 256 || id.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new DomainException("Versions.InvalidId", "Provider version ID must contain ASCII letters and digits", "use the provider's version ID", target: id);
    }
    private void Cleanup(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { lock (sync) diagnostics.Add(new("Metadata.ProviderCleanupFailed", path, exception.Message)); }
    }
    private static void GuardPath(string path, bool file = false)
    {
        for (FileSystemInfo? entry = file ? new FileInfo(path) : new DirectoryInfo(path); entry is not null;
            entry = entry is DirectoryInfo directory ? directory.Parent : ((FileInfo)entry).Directory)
        {
            if (!string.IsNullOrEmpty(entry.LinkTarget))
                throw new DomainException("Metadata.LinkedCachePath", "Provider cache path is linked", "use a regular private cache directory", "InvalidData", entry.FullName);
        }
    }
    private static DomainException Resource(string target, string? detail = null) => new("Metadata.ProviderRequestLimit", "Provider acquisition exceeds its resource limits",
        "narrow the requested batch or use explicit bounded quotas", "LimitsExceeded", target, details: detail);
    private static DomainException Invalid(string detail) => new("Metadata.InvalidProviderResponse", "Provider response does not contain valid requested version metadata",
        "retry or inspect the provider metadata", "InvalidData", details: detail);
    private DomainException WriteFailure(string detail) => new("Metadata.ProviderCacheWriteFailed", "Provider cache could not be updated",
        "check cache permissions and file locks", "WriteError", directory, details: detail);

    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (sync) { if (disposed) return; disposed = true; tasks = pending.Values.Select(value => (Task)value.Value).ToArray(); }
        lifetime.Cancel();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (Exception) { /* Task failures are observed by the acquisition callers. */ }
        finally { http.Dispose(); gate.Dispose(); lifetime.Dispose(); }
    }
}
