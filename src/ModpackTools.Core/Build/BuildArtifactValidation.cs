using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Build;

internal static class BuildArtifactValidation
{
    public static async Task VerifyAsync(BuildArtifactExpectation expected, MrpackDocument actual, string configurationDirectory,
        ArtifactProviderLimits? limits, string loader, CancellationToken cancellation, HttpMessageHandler? handler = null)
    {
        if (!StringComparer.InvariantCulture.Equals(expected.Name, actual.Name) || !StringComparer.InvariantCulture.Equals(expected.Version, actual.Version)
            || expected.Dependencies.Count != actual.Dependencies.Count || expected.Dependencies.Any(pair => !actual.Dependencies.TryGetValue(pair.Key, out var version) || version != pair.Value))
            throw Mismatch("Pack identity or loader dependencies differ from the frozen project");
        var files = actual.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var entries = actual.Entries.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in expected.PlainFiles)
        {
            cancellation.ThrowIfCancellationRequested(); var path = "overrides/" + file.Key;
            if (!entries.TryGetValue(path, out var item) || !Equal(file.Value, item.Hashes.GetValueOrDefault("sha256", ""))) throw Mismatch("Missing or changed override: " + file.Key);
            consumed.Add("entry:" + path);
        }
        await using var references = new ArtifactProvider(configurationDirectory, limits, cancellation, handler);
        var referenceHashes = new Dictionary<string, (string Sha1, string Sha512, string Declared, long Size)>(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedFile in expected.Managed)
        {
            cancellation.ThrowIfCancellationRequested(); var download = expectedFile.Download;
            if (files.TryGetValue(expectedFile.Path, out var file))
            {
                if (!Hex(file.Hashes.GetValueOrDefault("sha1", ""), 40) || !Hex(file.Hashes.GetValueOrDefault("sha512", ""), 128)
                    || file.Downloads.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
                    throw Mismatch("Managed manifest has invalid hashes or downloads: " + expectedFile.Path);
                VerifyProvider(expectedFile, file.Hashes, file.Size);
                if (file.Hashes.TryGetValue(download.Algorithm, out var hash))
                {
                    if (!Equal(download.Hash, hash)) throw Mismatch("Managed manifest hash differs: " + expectedFile.Path);
                }
                else
                {
                    var observed = await references.ReadAsync(new(download.Url, download.Hash, download.Algorithm, expectedFile.LocalPath, Check: true), loader).ConfigureAwait(false);
                    if (!observed.Available || observed.Path is null) throw Mismatch("Managed manifest hash cannot be independently verified: " + expectedFile.Path);
                    var referenceKey = download.Algorithm + ":" + observed.Path;
                    if (!referenceHashes.TryGetValue(referenceKey, out var hashes))
                    { hashes = Hash(observed.Path, download.Algorithm, limits?.MaximumArtifactBytes ?? 512L * 1024 * 1024, cancellation); referenceHashes.Add(referenceKey, hashes); }
                    if (!Equal(download.Hash, hashes.Declared) || !Equal(hashes.Sha1, file.Hashes.GetValueOrDefault("sha1", "")) || !Equal(hashes.Sha512, file.Hashes.GetValueOrDefault("sha512", "")) || hashes.Size != file.Size)
                        throw Mismatch("Managed manifest bytes differ from the verified reference: " + expectedFile.Path);
                }
                var installed = expectedFile.Optional ? "optional" : "required";
                if (file.Environment.GetValueOrDefault("client", "required") != (expectedFile.Side == "server" ? "unsupported" : installed)
                    || file.Environment.GetValueOrDefault("server", "required") != (expectedFile.Side == "client" ? "unsupported" : installed)) throw Mismatch("Managed manifest side or optional status differs: " + expectedFile.Path);
                if (file.Downloads.IsEmpty) throw Mismatch("Managed manifest has no download: " + expectedFile.Path);
                if (!string.IsNullOrEmpty(download.Url) && !file.Downloads.Any(value => SameUrl(download.Url, value))) throw Mismatch("Managed manifest download differs: " + expectedFile.Path);
                consumed.Add("manifest:" + expectedFile.Path);
            }
            else
            {
                var prefix = expectedFile.Side switch { "client" => "client-overrides/", "server" => "server-overrides/", _ => "overrides/" };
                var path = prefix + expectedFile.Path;
                if (!entries.TryGetValue(path, out var entry) || !Equal(download.Hash, entry.Hashes.GetValueOrDefault(download.Algorithm, "")))
                    throw Mismatch("Managed artifact is missing or has changed: " + expectedFile.Path);
                VerifyProvider(expectedFile, entry.Hashes, entry.Size);
                consumed.Add("entry:" + path);
            }
        }
        foreach (var file in actual.Files) if (!consumed.Contains("manifest:" + file.Path)) throw Mismatch("Unexpected managed manifest file: " + file.Path);
        foreach (var entry in actual.Entries) if (!consumed.Contains("entry:" + entry.Path)) throw Mismatch("Unexpected archive file: " + entry.Path);
    }
    private static bool Equal(string expected, string actual) => StringComparer.OrdinalIgnoreCase.Equals(expected, actual);
    private static bool Hex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
    private static void VerifyProvider(ExpectedManagedArtifact expected, ImmutableDictionary<string, string> hashes, long size)
    {
        if (expected.Provider is { } provider && (provider.Sha1.Length != 0 && !Equal(provider.Sha1, hashes.GetValueOrDefault("sha1", ""))
            || provider.Sha512.Length != 0 && !Equal(provider.Sha512, hashes.GetValueOrDefault("sha512", "")) || provider.Size is { } bytes && bytes != size))
            throw Mismatch("Managed artifact differs from verified provider metadata: " + expected.Path);
    }
    private static bool SameUrl(string left, string right) => Uri.TryCreate(left, UriKind.Absolute, out var first) && Uri.TryCreate(right, UriKind.Absolute, out var second)
        && first.Scheme is "http" or "https" && second.Scheme is "http" or "https" && Uri.Compare(first, second, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.Ordinal) == 0;
    private static DomainException Mismatch(string detail) => new("Build.ArtifactMismatch", "The generated artifact does not match the prepared project", "inspect the Packwiz export and retry", "InvalidResult", details: detail);
    private static (string Sha1, string Sha512, string Declared, long Size) Hash(string path, string algorithm, long maximum, CancellationToken cancellation)
    {
        WindowsProjectIdentity.GuardAncestors(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        WindowsProjectIdentity.AssertStreamBytes(file.SafeFileHandle, maximum, path);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1); using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        using var declared = IncrementalHash.CreateHash(algorithm switch { "md5" => HashAlgorithmName.MD5, "sha256" => HashAlgorithmName.SHA256, _ => throw Mismatch("Unsupported reference hash algorithm") });
        var buffer = ArrayPool<byte>.Shared.Rent(65536); long size = 0;
        try
        {
            int count; while ((count = file.Read(buffer, 0, 65536)) != 0)
            {
                cancellation.ThrowIfCancellationRequested(); if (count > maximum - size) throw Mismatch("Verified reference exceeds its byte quota");
                size += count; sha1.AppendData(buffer, 0, count); sha512.AppendData(buffer, 0, count); declared.AppendData(buffer, 0, count);
            }
            cancellation.ThrowIfCancellationRequested();
            return (Convert.ToHexString(sha1.GetHashAndReset()), Convert.ToHexString(sha512.GetHashAndReset()), Convert.ToHexString(declared.GetHashAndReset()), size);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
