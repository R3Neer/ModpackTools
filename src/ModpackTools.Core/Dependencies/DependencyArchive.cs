using System.Buffers;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Dependencies;

public static class DependencyArchive
{
    public static PreparedDependency Prepare(string archivePath, string stagingParent, DependencyPackage package,
        DependencyArchiveLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        limits ??= new();
        if (limits.MaximumArchiveBytes <= 0 || limits.MaximumExpandedBytes <= 0 || limits.MaximumLicenseBytes <= 0 || limits.MaximumEntries <= 0)
            throw Error("InvalidPolicy", "Dependency archive quotas must be positive", archivePath);
        if (string.IsNullOrWhiteSpace(package.Version) || package.Version.Length > 64 || !HashValue(package.ArchiveSha256) || !HashValue(package.ExecutableSha256)
            || !Leaf(package.Executable) || !Leaf(package.License) || StringComparer.OrdinalIgnoreCase.Equals(package.Executable, package.License))
            throw Error("InvalidManifest", "Trusted dependency metadata is invalid", archivePath);
        archivePath = Path.GetFullPath(archivePath); stagingParent = Path.GetFullPath(stagingParent);
        string? stage = null; var stageOwned = false; var owned = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(archivePath);
            using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, limits.MaximumArchiveBytes, archivePath);
            if (input.Length > limits.MaximumArchiveBytes) throw Error("ArchiveLimit", "Dependency archive exceeds its byte quota", archivePath);
            if (!StringComparer.OrdinalIgnoreCase.Equals(package.ArchiveSha256, Hash(input, limits.MaximumArchiveBytes, cancellation)))
                throw Error("ArchiveHashMismatch", "Downloaded Packwiz archive failed SHA-256 verification", archivePath, "SecurityError");
            input.Position = 0;
            _ = ArchivePreflight.Validate(input, new(MaximumCentralDirectoryBytes: 1024 * 1024, MaximumEntries: limits.MaximumEntries));
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > limits.MaximumEntries) throw Error("ArchiveLimit", "Dependency archive exceeds its entry quota", archivePath);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); ZipArchiveEntry? executable = null; ZipArchiveEntry? license = null; long total = 0;
            // Validate every central-directory path before materializing even the
            // selected leaves. Never extract arbitrary archive entries.
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested(); var name = entry.FullName.Replace('\\', '/');
                var directory = name.EndsWith('/'); var relative = directory ? name[..^1] : name;
                if (!SafeRelative(relative) || !names.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000
                    || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw Error("ArchiveInvalid", "Dependency archive contains an unsafe or duplicate path", entry.FullName);
                if (entry.Length < 0 || entry.Length > limits.MaximumExpandedBytes - total) throw Error("ArchiveLimit", "Dependency archive exceeds its expanded byte quota", entry.FullName);
                total += entry.Length;
                if (directory) { if (entry.Length != 0) throw Error("ArchiveInvalid", "Archive directory contains data", entry.FullName); continue; }
                if (StringComparer.Ordinal.Equals(name, package.Executable)) executable = entry;
                if (StringComparer.Ordinal.Equals(name, package.License)) license = entry;
            }
            if (executable is null || license is null || license.Length == 0)
                throw Error("ArchiveIncomplete", "Verified Packwiz archive does not contain the expected executable and license", archivePath);
            if (license.Length > limits.MaximumLicenseBytes) throw Error("ArchiveLimit", "Dependency license exceeds its byte quota", package.License);
            WindowsProjectIdentity.GuardAncestors(stagingParent); System.IO.Directory.CreateDirectory(stagingParent);
            using var parentIdentity = WindowsProjectIdentity.Open(stagingParent);
            stage = Path.Combine(stagingParent, ".modpacktools-package-" + Guid.NewGuid().ToString("N"));
            if (!WindowsDirectoryReservation.TryCreate(stage)) throw Error("StageCreateFailed", "Dependency staging directory could not be reserved", stage);
            stageOwned = true; WindowsProjectIdentity.GuardAncestors(stage);
            var files = ImmutableArray.CreateBuilder<PreparedDependencyFile>();
            foreach (var entry in new[] { executable, license })
            {
                cancellation.ThrowIfCancellationRequested(); var path = TransactionFiles.Within(stage, entry.FullName);
                using var source = entry.Open();
                using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                owned.Add(path, null); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var length = Copy(source, destination, hash, entry.Length, cancellation); destination.Flush(flushToDisk: true);
                var digest = Convert.ToHexString(hash.GetHashAndReset());
                owned[path] = digest;
                if (entry == executable && !StringComparer.OrdinalIgnoreCase.Equals(digest, package.ExecutableSha256))
                    throw Error("ExecutableHashMismatch", "Packwiz executable failed SHA-256 verification", archivePath, "SecurityError");
                files.Add(new(entry.FullName, digest, length));
            }
            parentIdentity.AssertCurrent(); cancellation.ThrowIfCancellationRequested(); return new(stage, package.Version, files.ToImmutable());
        }
        catch (Exception error)
        {
            if (stageOwned && stage is not null)
            {
                try
                {
                    WindowsProjectIdentity.GuardAncestors(stage);
                    var entries = System.IO.Directory.EnumerateFileSystemEntries(stage).Take(3).ToArray();
                    if (entries.Any(path => !owned.ContainsKey(path))) throw new IOException("Dependency stage contains an unexpected file; retained: " + stage);
                    foreach (var pair in owned)
                        if (File.Exists(pair.Key) && (pair.Value is null || !StringComparer.Ordinal.Equals(pair.Value, TransactionFiles.Hash(pair.Key, limits.MaximumExpandedBytes))))
                            throw new IOException("Dependency stage file could not be verified for cleanup: " + pair.Key);
                    foreach (var path in owned.Keys) { WindowsProjectIdentity.GuardAncestors(path); File.Delete(path); }
                    System.IO.Directory.Delete(stage, recursive: false);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
                {
                    var retained = new DomainException("Dependency.StageRetained", "Dependency preparation failed and its stage must be retained", "inspect the retained stage before retrying", "InvalidOperation", stage, details: error.Message + "; " + cleanup.Message);
                    retained.Data["ModpackTools.RetainStage"] = true; throw retained;
                }
            }
            if (error is OperationCanceledException) throw new DomainException("Core.OperationCancelled", "Dependency preparation was cancelled", "retry when ready", "OperationStopped", archivePath);
            if (error is ArchiveLimitException) throw Error("ArchiveLimit", error.Message, archivePath, "LimitsExceeded");
            if (error is InvalidDataException) throw Error("ArchiveInvalid", "The verified Packwiz archive could not be read", archivePath, "InvalidData");
            throw;
        }
    }
    private static string Hash(Stream stream, long maximum, CancellationToken cancellation)
    { using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); _ = Copy(stream, Stream.Null, hash, maximum, cancellation, exact: false); return Convert.ToHexString(hash.GetHashAndReset()); }
    private static long Copy(Stream source, Stream destination, IncrementalHash hash, long maximum, CancellationToken cancellation, bool exact = true)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536); long total = 0;
        try
        {
            int read; while ((read = source.Read(buffer, 0, 65536)) != 0)
            { cancellation.ThrowIfCancellationRequested(); if (read > maximum - total) throw Error("ArchiveLimit", "Dependency data exceeds its declared byte quota", null); destination.Write(buffer, 0, read); hash.AppendData(buffer.AsSpan(0, read)); total += read; }
            cancellation.ThrowIfCancellationRequested(); if (exact && total != maximum) throw Error("ArchiveInvalid", "Dependency entry length differs from its declaration", null); return total;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    private static bool HashValue(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool Leaf(string? value) => value is { Length: > 0 and <= 128 } && !value.Contains('/') && !value.Contains('\\') && SafeRelative(value);
    private static bool SafeRelative(string value) => value.Length is > 0 and <= 1024 && !Path.IsPathRooted(value) && !value.Contains(':')
        && value.Split('/').All(part => part.Length != 0 && part is not "." and not ".." && !part.EndsWith('.') && !part.EndsWith(' ') && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !Reserved(part));
    private static bool Reserved(string part)
    {
        var name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" || name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] is >= '0' and <= '9';
    }
    private static DomainException Error(string id, string message, string? path, string category = "InvalidData") => new("Dependency." + id, message, "retry with the verified Packwiz package", category, path);
}
