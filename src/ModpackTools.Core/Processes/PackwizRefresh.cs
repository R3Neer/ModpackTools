using System.Buffers;
using System.Security.Cryptography;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Processes;

public sealed record PackwizObservation(bool Available, string? Path, string Source, string? Version);
public sealed record PackwizRefreshReceipt(PackwizObservation Tool, string IndexFile, string HashAlgorithm, string IndexHash, NativeToolResult Output);

public static class PackwizRefresh
{
    public static PackwizObservation Resolve(string? configuredPath, string? pathValue, string? managedPath, string? managedVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configured = Path.GetFullPath(configuredPath); return new(File.Exists(configured), configured, "configured", null);
        }
        foreach (var entry in (pathValue ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(entry.Trim('"'), "packwiz.exe"));
                if (File.Exists(candidate)) return new(true, candidate, "PATH", null);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { }
        }
        if (!string.IsNullOrWhiteSpace(managedPath) && File.Exists(managedPath)) return new(true, Path.GetFullPath(managedPath), "managed", managedVersion);
        return new(false, null, "missing", null);
    }

    public static async Task<PackwizRefreshReceipt> RefreshAsync(ProjectContext stage, PackwizObservation tool, string nativeHost,
        NativeToolLimits? limits = null, CancellationToken cancellation = default)
    {
        if (!tool.Available || tool.Path is null) throw new DomainException("Dependency.PackwizNotFound", "Required Packwiz executable is not available",
            "configure an existing native Packwiz executable", "ObjectNotFound", tool.Path);
        WindowsProjectIdentity.GuardAncestors(stage.Root);
        var output = await NativeTool.RunAsync(tool.Path, ["refresh"], Path.GetFullPath(stage.Root), nativeHost, limits, cancellation).ConfigureAwait(false);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var manifestPath = TransactionFiles.Within(stage.Root, "pack.toml");
            var manifest = TomlDeclarationReader.Read(TextFile.Read(manifestPath), new());
            if (manifest.Get("index") is not TomlFields index) throw new FormatException("Refreshed manifest has no index table.");
            var relative = String(index.Get("file")); if (relative.Length == 0) relative = "index.toml";
            var path = TransactionFiles.Within(stage.Root, relative);
            var algorithm = String(index.Get("hash-format")); var expected = String(index.Get("hash"));
            var name = algorithm.ToLowerInvariant() switch
            {
                "sha256" => HashAlgorithmName.SHA256,
                "sha512" => HashAlgorithmName.SHA512,
                "sha1" => HashAlgorithmName.SHA1,
                "md5" => HashAlgorithmName.MD5,
                _ => throw new FormatException("Refreshed index uses an unsupported hash algorithm.")
            };
            _ = TomlDeclarationReader.Read(TextFile.Read(path), new());
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var hash = IncrementalHash.CreateHash(name); var buffer = ArrayPool<byte>.Shared.Rent(65536); long bytes = 0;
            string actual;
            try
            {
                int read;
                while ((read = file.Read(buffer, 0, 65536)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested(); bytes += read;
                    if (bytes > 8 * 1024 * 1024) throw new FormatException("Refreshed index exceeds the8MiB quota.");
                    hash.AppendData(buffer, 0, read);
                }
                actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            if (!StringComparer.OrdinalIgnoreCase.Equals(expected, actual)) throw new FormatException("Refreshed index hash differs from the actual file bytes.");
            return new(tool, relative.Replace('\\', '/'), algorithm, actual, output);
        }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Packwiz refresh validation was cancelled", "retry when ready", "OperationStopped"); }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
        { throw new DomainException("Build.InvalidRefresh", "Packwiz exited successfully but its refreshed index is invalid", "inspect the stage manifest and index", "InvalidResult", stage.Root, details: error.Message); }
    }
    private static string String(TomlDeclarationValue value) => value is TomlText text ? text.Value : "";
}
