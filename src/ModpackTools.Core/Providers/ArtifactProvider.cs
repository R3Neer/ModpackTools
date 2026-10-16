using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Source;

namespace ModpackTools.Core.Providers;

public sealed record ArtifactRequest(string Url, string Hash, string Algorithm, string? LocalPath = null, bool Check = false);
public enum VerifiedArtifactSource { Local, Cache, Download }
public sealed record VerifiedArtifactObservation(VerifiedArtifactSource? Source, string? Path,
    string Algorithm, string Hash, ArtifactArchiveDeclarations? Declarations)
{
    public bool Available => Source is not null;
}
public sealed record ArtifactProviderLimits(long MaximumArtifactBytes = 512L * 1024 * 1024,
    long MaximumTotalBytes = 4L * 1024 * 1024 * 1024, int MaximumRequests = 1000,
    int MaximumConcurrency = 4, int TimeoutMilliseconds = 60000, int MaximumOperations = 10000);
public sealed record ArtifactProviderStatistics(int Requests, long NetworkBytes, long VerificationBytes);

public sealed class ArtifactProvider : IAsyncDisposable
{
    private readonly string directory;
    private readonly ArtifactProviderLimits limits;
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate;
    private readonly CancellationTokenSource lifetime;
    private readonly object sync = new();
    private readonly HashSet<Task> pending = [];
    private readonly Dictionary<string, SemaphoreSlim> identities = new(StringComparer.Ordinal);
    private readonly List<ProviderAcquisitionDiagnostic> diagnostics = [];
    private long bytes, reserved, networkBytes, verificationBytes;
    private int requests, operations;
    private bool disposed;

    public ArtifactProvider(string configurationDirectory, ArtifactProviderLimits? limits = null,
        CancellationToken cancellation = default, HttpMessageHandler? handler = null)
    {
        this.limits = limits ?? new();
        var invalid = this.limits.MaximumArtifactBytes <= 0 ? nameof(this.limits.MaximumArtifactBytes)
            : this.limits.MaximumTotalBytes <= 0 ? nameof(this.limits.MaximumTotalBytes)
            : this.limits.MaximumRequests <= 0 ? nameof(this.limits.MaximumRequests)
            : this.limits.MaximumOperations <= 0 ? nameof(this.limits.MaximumOperations)
            : this.limits.MaximumConcurrency is < 1 or > 64 ? nameof(this.limits.MaximumConcurrency)
            : this.limits.TimeoutMilliseconds <= 0 ? nameof(this.limits.TimeoutMilliseconds) : null;
        if (invalid is not null) throw new DomainException("Metadata.InvalidArtifactPolicy", "Artifact limits are invalid",
            "use positive quotas and concurrency between1 and64", target: invalid);
        directory = Path.Combine(Path.GetFullPath(configurationDirectory), "artifacts");
        gate = new(this.limits.MaximumConcurrency, this.limits.MaximumConcurrency);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        http = new(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = this.limits.MaximumConcurrency
        }, disposeHandler: true)
        { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("R3Neer-ModpackTools/" + ProductVersion.Current.Version);
    }

    public ArtifactProviderStatistics Statistics { get { lock (sync) return new(requests, networkBytes, verificationBytes); } }
    public ImmutableArray<ProviderAcquisitionDiagnostic> Diagnostics { get { lock (sync) return [.. diagnostics]; } }

    public Task<VerifiedArtifactObservation> ReadAsync(ArtifactRequest request, string loader, ArtifactTraversalLimits? traversal = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(loader);
        traversal = ArtifactArchiveReader.ValidatePolicy(traversal);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (operations >= limits.MaximumOperations) throw Limit(nameof(limits.MaximumOperations));
            operations++;
            var task = ReadCore(request, loader, traversal);
            pending.Add(task);
            _ = task.ContinueWith(completed => { lock (sync) pending.Remove(completed); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<VerifiedArtifactObservation> ReadCore(ArtifactRequest request, string loader, ArtifactTraversalLimits? traversal)
    {
        await Task.Yield();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(limits.TimeoutMilliseconds);
        var token = deadline.Token;
        var acquired = false;
        SemaphoreSlim? identityGate = null;
        var identityAcquired = false;
        string? temporary = null;
        var owned = false;
        var receiving = false;
        long heldReservation = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            var algorithm = request.Algorithm.ToLowerInvariant() switch
            {
                "sha1" => HashAlgorithmName.SHA1,
                "sha256" => HashAlgorithmName.SHA256,
                "sha512" => HashAlgorithmName.SHA512,
                "md5" => HashAlgorithmName.MD5,
                _ => default
            };
            if (string.IsNullOrEmpty(request.Hash) || algorithm.Name is null) return Missing();
            var length = algorithm == HashAlgorithmName.MD5 ? 32 : algorithm == HashAlgorithmName.SHA1 ? 40 : algorithm == HashAlgorithmName.SHA256 ? 64 : 128;
            if (request.Hash.Length != length || request.Hash.Any(character => !char.IsAsciiHexDigit(character)))
                throw new DomainException("Content.InvalidHash", "Artifact digest has invalid syntax", "use the provider's complete hexadecimal digest", "InvalidData");
            var identity = TextHash.Compute(request.Algorithm + ":" + request.Hash);
            lock (sync)
            {
                if (!identities.TryGetValue(identity, out identityGate)) identities.Add(identity, identityGate = new(1, 1));
            }
            await identityGate.WaitAsync(token).ConfigureAwait(false); identityAcquired = true;
            await gate.WaitAsync(token).ConfigureAwait(false); acquired = true;
            if (!string.IsNullOrEmpty(request.LocalPath))
            {
                var local = await ReadVerified(request.LocalPath, VerifiedArtifactSource.Local).ConfigureAwait(false);
                if (local is not null) return local;
            }
            long downloadedBytes;
            var path = Path.Combine(directory, identity);
            Guard(path);
            var cached = await ReadVerified(path, VerifiedArtifactSource.Cache).ConfigureAwait(false);
            if (cached is not null) return cached;
            if (!request.Check || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return Missing();
            if (uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                throw new DomainException("Content.InvalidArtifactUrl", "Artifact URL contains credentials or a fragment", "use the provider's direct HTTP(S) download URL", "InvalidData");
            lock (sync)
            {
                if (requests >= limits.MaximumRequests) throw Limit(nameof(limits.MaximumRequests));
                requests++;
            }
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            Guard(directory); Directory.CreateDirectory(directory); Guard(directory);
            temporary = Path.Combine(directory, ".download-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                owned = true;
                using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var hash = IncrementalHash.CreateHash(algorithm);
                var buffer = new byte[8192]; long localBytes = 0;
                while (true)
                {
                    receiving = true;
                    var read = await body.ReadAsync(buffer.AsMemory(0, Allowance(localBytes)), token).ConfigureAwait(false);
                    receiving = false;
                    if (read == 0) break;
                    Charge(read, ref localBytes, true, ref heldReservation); hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                }
                if (!Matches(hash.GetHashAndReset())) throw new DomainException("Content.HashMismatch", "Downloaded artifact hash does not match its metadata",
                    "retry after checking the provider metadata", "InvalidData", details: request.Url);
                await file.FlushAsync(token).ConfigureAwait(false);
                downloadedBytes = localBytes;
            }
            lock (sync)
            {
                if (downloadedBytes > limits.MaximumTotalBytes - bytes - reserved) throw Limit(nameof(limits.MaximumTotalBytes));
                reserved += downloadedBytes; heldReservation = downloadedBytes;
            }
            token.ThrowIfCancellationRequested(); Guard(path);
            File.Move(temporary, path, overwrite: true); owned = false;
            return await ReadVerified(path, VerifiedArtifactSource.Download, useReservation: true).ConfigureAwait(false)
                ?? throw new DomainException("Content.ArtifactChanged", "Artifact changed after cache replacement", "retry after checking other cache writers", "InvalidData", path);

            async Task<VerifiedArtifactObservation?> ReadVerified(string candidate, VerifiedArtifactSource source, bool useReservation = false)
            {
                FileStream file;
                try { file = new(candidate, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous); }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return null; }
                catch (UnauthorizedAccessException) when (Directory.Exists(candidate)) { return null; }
                using (file)
                {
                    using var hash = IncrementalHash.CreateHash(algorithm);
                    var buffer = new byte[8192]; long localBytes = 0;
                    while (true)
                    {
                        var read = await file.ReadAsync(buffer.AsMemory(0, Allowance(localBytes, useReservation ? heldReservation : 0)), token).ConfigureAwait(false);
                        if (read == 0) break;
                        Charge(read, ref localBytes, false, ref heldReservation); hash.AppendData(buffer, 0, read);
                    }
                    if (!Matches(hash.GetHashAndReset())) return null;
                    token.ThrowIfCancellationRequested(); file.Position = 0;
                    var declarations = ArtifactArchiveReader.Read(file, candidate, loader, traversal);
                    token.ThrowIfCancellationRequested();
                    return new(source, candidate, request.Algorithm, request.Hash, declarations);
                }
            }
            bool Matches(byte[] actual) => Convert.ToHexString(actual).Equals(request.Hash, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw lifetime.IsCancellationRequested
                ? new DomainException("Core.OperationCancelled", "Artifact lookup was cancelled", "retry when ready", "OperationStopped")
                : new DomainException("Content.ArtifactTimeout", "Artifact lookup exceeded its deadline", "check the connection or use an explicit bounded timeout", "OperationTimeout");
        }
        catch (HttpRequestException exception)
        { throw new DomainException("Content.ArtifactRequestFailed", "Artifact request could not be completed", "check the connection and retry", "ConnectionError", details: exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DomainException(receiving ? "Content.ArtifactRequestFailed" : "Content.ArtifactIoFailed", "Artifact could not be read or stored",
            "check file permissions, locks and the connection", receiving ? "ConnectionError" : "ReadError", details: exception.Message);
        }
        finally
        {
            lock (sync) reserved -= heldReservation;
            if (owned && temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { lock (sync) diagnostics.Add(new("Content.ArtifactCleanupFailed", temporary, exception.Message)); }
            }
            if (acquired) gate.Release();
            if (identityAcquired) identityGate!.Release();
        }
        VerifiedArtifactObservation Missing() => new(null, null, request.Algorithm, request.Hash, null);
    }

    private int Allowance(long localBytes, long ownedReservation = 0)
    {
        lock (sync)
        {
            var remaining = Math.Min(limits.MaximumArtifactBytes - localBytes, limits.MaximumTotalBytes - bytes - reserved + ownedReservation);
            if (remaining < 0) throw Limit(nameof(limits.MaximumTotalBytes));
            return (int)Math.Min(8192, remaining >= 8192 ? 8192 : remaining + 1);
        }
    }
    private void Charge(int count, ref long localBytes, bool network, ref long ownedReservation)
    {
        lock (sync)
        {
            var consumed = Math.Min(count, ownedReservation);
            reserved -= consumed; ownedReservation -= consumed;
            bytes = count > long.MaxValue - bytes ? long.MaxValue : bytes + count;
            if (network) networkBytes = count > long.MaxValue - networkBytes ? long.MaxValue : networkBytes + count;
            else verificationBytes = count > long.MaxValue - verificationBytes ? long.MaxValue : verificationBytes + count;
            if (count > limits.MaximumArtifactBytes - localBytes) throw Limit(nameof(limits.MaximumArtifactBytes));
            if (bytes > limits.MaximumTotalBytes - reserved) throw Limit(nameof(limits.MaximumTotalBytes));
            localBytes += count;
        }
    }
    private static void Guard(string path)
    {
        for (FileSystemInfo? entry = new FileInfo(path); entry is not null;
            entry = entry is DirectoryInfo folder ? folder.Parent : ((FileInfo)entry).Directory)
            if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new DomainException("Content.LinkedArtifactCache", "Artifact cache path is linked",
                "use a regular private cache directory", "InvalidData", entry.FullName);
    }
    private static DomainException Limit(string target) => new("Content.ArtifactLimit", "Artifact lookup exceeds its resource limit",
        "use explicit bounded quotas or smaller artifacts", "LimitsExceeded", target);

    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (sync) { if (disposed) return; disposed = true; tasks = [.. pending]; }
        lifetime.Cancel();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (Exception) { /* Operation callers observe their own failures. */ }
        finally { http.Dispose(); lifetime.Dispose(); gate.Dispose(); foreach (var identity in identities.Values) identity.Dispose(); }
    }
}
