using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ModpackTools.Core.Distribution;

public sealed record NativeReleaseAsset(string Name, string Url, string Sha256, long Length);
public sealed record NativeRelease(string Version, string Tag, string Url, NativeReleaseAsset? Asset, string? UnavailableReason);

public static class NativeReleaseChannel
{
    public const string LatestUri = "https://api.github.com/repos/R3Neer/ModpackTools/releases/latest";
    private const int MaximumMetadataBytes = 512 * 1024;

    public static async Task<NativeRelease> LatestAsync(CancellationToken cancellation = default, HttpMessageHandler? handler = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(10000);
        using var client = new HttpClient(handler ?? new HttpClientHandler
        { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 64 })
        { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ModpackTools/" + ProductVersion.Current.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        try
        {
            using var response = await client.GetAsync(LatestUri, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) throw Error("InvalidRelease", "The official release endpoint redirected; its identity must be reviewed");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } declared && declared > MaximumMetadataBytes)
                throw Error("MetadataLimit", "Official release metadata exceeds its byte quota");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var output = new MemoryStream(); var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(0, 8192), deadline.Token).ConfigureAwait(false)) != 0)
                {
                    if (count > MaximumMetadataBytes - output.Length) throw Error("MetadataLimit", "Official release metadata exceeds its byte quota");
                    output.Write(buffer, 0, count);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            if (response.Content.Headers.ContentLength is { } expected && output.Length != expected)
                throw Error("CheckFailed", "Official release metadata ended before its declared length");
            deadline.Token.ThrowIfCancellationRequested(); return Parse(output.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw cancellation.IsCancellationRequested
                ? new DomainException("Core.OperationCancelled", "Release check was cancelled", "retry when ready", "OperationStopped", LatestUri)
                : Error("CheckTimeout", "The official release check exceeded its deadline");
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        { throw Error("CheckFailed", "Could not read the official stable release", error.Message); }
    }

    public static NativeRelease Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumMetadataBytes) throw Error("MetadataLimit", "Official release metadata exceeds its byte quota");
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 32 }); var root = document.RootElement;
            Unique(root); var tag = root.GetProperty("tag_name").GetString()!;
            if (tag is null || tag.Length is < 6 or > 40 || tag[0] != 'v' || root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
                throw Error("InvalidRelease", "The official endpoint did not return a stable versioned release");
            var version = tag[1..]; var pieces = version.Split('.');
            if (pieces.Length != 3 || pieces.Any(piece => piece.Length == 0 || piece.Length > 10 || piece.Length > 1 && piece[0] == '0'
                || !int.TryParse(piece, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                throw Error("InvalidRelease", "Stable release tags must use canonical vMAJOR.MINOR.PATCH versions");
            var releaseUrl = "https://github.com/R3Neer/ModpackTools/releases/tag/" + tag;
            if (root.GetProperty("html_url").GetString() != releaseUrl) throw Error("InvalidRelease", "Stable release location is not the official repository");
            var assets = root.GetProperty("assets");
            if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 64) throw Error("InvalidRelease", "Release asset list is invalid or exceeds its quota");
            var name = "ModpackTools-Native-win-x64-" + version + ".zip"; NativeReleaseAsset? selected = null;
            foreach (var asset in assets.EnumerateArray())
            {
                Unique(asset);
                if (asset.GetProperty("name").GetString() != name) continue;
                if (selected is not null) throw Error("InvalidAsset", "The stable release contains duplicate native Windows packages");
                var url = "https://github.com/R3Neer/ModpackTools/releases/download/" + tag + "/" + name;
                var digest = asset.GetProperty("digest").GetString(); var length = asset.GetProperty("size").GetInt64();
                if (asset.GetProperty("browser_download_url").GetString() != url || asset.GetProperty("state").GetString() != "uploaded"
                    || digest is not { Length: 71 } || !digest.StartsWith("sha256:", StringComparison.Ordinal) || !NativePackageManifest.HashValue(digest[7..])
                    || length is <= 0 or > 256L * 1024 * 1024)
                    throw Error("InvalidAsset", "Native release asset URL, digest, upload state or size is invalid");
                selected = new(name, url, digest[7..].ToUpperInvariant(), length);
            }
            return new(version, tag, releaseUrl, selected, selected is null ? "This stable release has no native Windows x64 package; legacy PowerShell packages are not installed" : null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { throw Error("InvalidRelease", "Official stable release metadata is invalid", error.Message); }
    }

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("InvalidRelease", "Release metadata field must be an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) if (!names.Add(property.Name)) throw Error("InvalidRelease", "Release metadata contains a duplicate field: " + property.Name);
    }

    private static DomainException Error(string id, string message, string? details = null) =>
        new("SelfUpdate." + id, message, "retry modpack self-update --check using the official release channel", "InvalidData", LatestUri, details: details);
}
