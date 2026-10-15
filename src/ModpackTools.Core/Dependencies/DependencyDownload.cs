using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Dependencies;

public sealed record DependencyDownloadLimits(long MaximumBytes = 256 * 1024 * 1024, int TimeoutMilliseconds = 60000, int MaximumRedirects = 5);
public sealed record DownloadedDependency(string Path, string Sha256, long Length);

public static class DependencyDownload
{
    public static async Task<DownloadedDependency> FetchAsync(Uri uri, string expectedSha256, string stagingParent,
        DependencyDownloadLimits? limits = null, CancellationToken cancellation = default, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        limits ??= new();
        if (limits.MaximumBytes <= 0 || limits.TimeoutMilliseconds <= 0 || limits.MaximumRedirects is < 0 or > 16)
            throw Error("InvalidDownloadPolicy", "Dependency download quotas must be positive and redirects bounded", stagingParent);
        if (expectedSha256 is not { Length: 64 } || expectedSha256.Any(value => !Uri.IsHexDigit(value)))
            throw Error("InvalidManifest", "Trusted archive SHA-256 is invalid", uri.ToString());
        ValidateUri(uri);
        string? path = null; var owned = false; WindowsProjectIdentity? parentIdentity = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(limits.TimeoutMilliseconds);
        using var http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 64 })
        { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ModpackTools/" + ProductVersion.Current.Version);
        try
        {
            var current = uri; var redirects = 0;
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (++redirects > limits.MaximumRedirects || response.Headers.Location is not { } location)
                        throw Error("DownloadRedirectLimit", "Dependency download redirect chain is invalid or exceeds its limit", current.ToString());
                    current = location.IsAbsoluteUri ? location : new Uri(current, location); ValidateUri(current); continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } declared && (declared < 0 || declared > limits.MaximumBytes))
                    throw Error("DownloadLimit", "Dependency response exceeds its byte quota", current.ToString(), "LimitsExceeded");
                stagingParent = FileTreeTransaction.CanonicalFutureDirectory(stagingParent); Directory.CreateDirectory(stagingParent);
                parentIdentity = WindowsProjectIdentity.Open(stagingParent);
                path = System.IO.Path.Combine(parentIdentity.Value.CanonicalRoot, ".modpacktools-download-" + Guid.NewGuid().ToString("N") + ".zip");
                string digest; long length = 0;
                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    owned = true;
                    await using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = ArrayPool<byte>.Shared.Rent(65536);
                    try
                    {
                        int count;
                        while ((count = await source.ReadAsync(buffer.AsMemory(0, 65536), deadline.Token).ConfigureAwait(false)) != 0)
                        {
                            if (count > limits.MaximumBytes - length) throw Error("DownloadLimit", "Dependency response exceeds its byte quota", current.ToString(), "LimitsExceeded");
                            await destination.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                            hash.AppendData(buffer.AsSpan(0, count)); length += count;
                        }
                    }
                    finally { ArrayPool<byte>.Shared.Return(buffer); }
                    digest = Convert.ToHexString(hash.GetHashAndReset());
                    if (!StringComparer.OrdinalIgnoreCase.Equals(digest, expectedSha256))
                        throw new DomainException("Dependency.ArchiveHashMismatch", "Downloaded Packwiz archive failed SHA-256 verification", "retry the verified package download", "SecurityError", current.ToString(), details: "Expected " + expectedSha256 + "; received " + digest);
                    if (response.Content.Headers.ContentLength is { } expectedLength && length != expectedLength)
                        throw Error("DownloadIncomplete", "Dependency response ended before its declared length", current.ToString(), "ConnectionError");
                    deadline.Token.ThrowIfCancellationRequested(); destination.Flush(flushToDisk: true);
                }
                parentIdentity.AssertCurrent(); deadline.Token.ThrowIfCancellationRequested(); owned = false;
                return new(path, digest, length);
            }
        }
        catch (Exception error)
        {
            if (owned && path is not null)
                try { parentIdentity?.AssertCurrent(); WindowsProjectIdentity.GuardAncestors(path); File.Delete(path); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
                {
                    var retained = new DomainException("Dependency.DownloadRetained", "Dependency download failed and its temporary file could not be removed", "inspect the retained download before retrying", "WriteError", path, details: error.Message + "; " + cleanup.Message);
                    retained.Data["ModpackTools.RetainStage"] = true; throw retained;
                }
            if (error is OperationCanceledException)
                throw cancellation.IsCancellationRequested
                    ? new DomainException("Core.OperationCancelled", "Dependency download was cancelled", "retry when ready", "OperationStopped", uri.ToString())
                    : Error("DownloadTimeout", "Dependency download exceeded its deadline", uri.ToString(), "OperationTimeout");
            if (error is HttpRequestException)
                throw new DomainException("Dependency.DownloadFailed", "The verified Packwiz archive could not be downloaded", "check the network connection and retry", "ConnectionError", uri.ToString(), details: error.Message);
            if (error is IOException or UnauthorizedAccessException)
                throw new DomainException("Dependency.DownloadWriteFailed", "Dependency download could not be stored", "check the temporary destination and retry", "WriteError", path ?? stagingParent, details: error.Message);
            throw;
        }
        finally { parentIdentity?.Dispose(); }
    }
    private static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw Error("InvalidDownloadUri", "Dependency download requires an absolute HTTPS URI without credentials or fragment", uri.ToString());
    }
    private static DomainException Error(string id, string message, string path, string category = "InvalidData") => new("Dependency." + id, message, "use the pinned dependency package and bounded download policy", category, path);
}
