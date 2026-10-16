using System.Buffers;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

public sealed record NativePackageLimits(long MaximumArchiveBytes = 256L * 1024 * 1024,
    long MaximumExpandedBytes = 512L * 1024 * 1024, int MaximumEntries = 64,
    int MaximumManifestBytes = 64 * 1024, long MaximumExecutableBytes = 128L * 1024 * 1024,
    long MaximumNuBytes = 2L * 1024 * 1024, long MaximumJsonBytes = 8L * 1024 * 1024,
    long MaximumTextBytes = 1024 * 1024);

public sealed record NativePackageProgress(string Phase, string Path, long CompletedBytes, long TotalBytes,
    int CompletedFiles, int TotalFiles);

public sealed record NativePreparedPackage(string Directory, ProjectPhysicalIdentity Identity,
    NativePackageManifest Manifest, ImmutableArray<NativePackageFile> Files, string ArchiveSha256);

public static class NativePackageArchive
{
    public static NativePreparedPackage OpenInstalled(string directory, NativeVersionReference reference, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        using var identity = WindowsProjectIdentity.Open(directory);
        if (identity.Value.FileIdentity != reference.FileIdentity) throw Error("StageChanged", "Installed version directory identity changed", directory);
        var path = Path.Combine(directory, "package.json"); WindowsProjectIdentity.GuardAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, 64 * 1024, path, defaultStreamOnly: true);
        if (input.Length > 64 * 1024) throw Error("ArchiveLimit", "Installed native manifest exceeds its byte quota", path);
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        var manifest = NativePackageManifest.Read(bytes);
        if (manifest.BuildId != reference.BuildId || manifest.Version != reference.Version) throw Error("StageChanged", "Installed native manifest does not match active control", path);
        var canonical = manifest.ToBytes();
        var files = manifest.Files.Add(new("package.json", canonical.Length, Convert.ToHexString(SHA256.HashData(canonical))))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToImmutableArray();
        var result = new NativePreparedPackage(directory, identity.Value, manifest, files, reference.ArchiveSha256);
        Verify(result, cancellation: cancellation); identity.AssertCurrent(); return result;
    }

    public static NativePreparedPackage Prepare(string archivePath, string expectedSha256, string stagingParent,
        NativePackageLimits? limits = null, CancellationToken cancellation = default, Action<NativePackageProgress>? progress = null)
    {
        limits ??= new(); ValidateLimits(limits);
        if (!NativePackageManifest.HashValue(expectedSha256)) throw Error("InvalidArchiveHash", "Trusted archive SHA-256 is required", archivePath);
        archivePath = Path.GetFullPath(archivePath); stagingParent = Path.GetFullPath(stagingParent);
        string? stage = null; ProjectPhysicalIdentity? stageIdentity = null;
        var owned = new Dictionary<string, NativePackageFile?>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(archivePath);
            using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, limits.MaximumArchiveBytes, archivePath);
            if (input.Length > limits.MaximumArchiveBytes) throw Error("ArchiveLimit", "Native archive exceeds its byte quota", archivePath);
            var archiveDigest = Copy(input, Stream.Null, input.Length, cancellation,
                bytes => progress?.Invoke(new("hash", archivePath, bytes, input.Length, 0, 1)));
            if (!StringComparer.OrdinalIgnoreCase.Equals(expectedSha256, archiveDigest))
                throw Error("ArchiveHashMismatch", "Native archive failed trusted SHA-256 verification", archivePath);
            input.Position = 0;
            _ = ArchivePreflight.Validate(input, new(MaximumCentralDirectoryBytes: 1024 * 1024, MaximumEntries: limits.MaximumEntries));
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var entries = ValidateArchive(archive, limits);
            var manifestEntry = entries["package.json"];
            byte[] manifestBytes;
            using (var source = manifestEntry.Open())
            using (var memory = new MemoryStream())
            { _ = Copy(source, memory, manifestEntry.Length, cancellation); manifestBytes = memory.ToArray(); }
            var manifest = NativePackageManifest.Read(manifestBytes);
            foreach (var file in manifest.Files)
                if (entries[file.Path].Length != file.Length) throw Error("EntryLengthMismatch", "Native payload length differs from its manifest", file.Path);
            var canonical = manifest.ToBytes();
            if (canonical.Length > limits.MaximumManifestBytes) throw Error("ArchiveLimit", "Canonical manifest exceeds its byte quota", "package.json");
            var total = manifest.Files.Sum(file => file.Length) + canonical.Length;
            if (total > limits.MaximumExpandedBytes) throw Error("ArchiveLimit", "Canonical native package exceeds its expanded byte quota", archivePath);
            WindowsProjectIdentity.GuardAncestors(stagingParent); System.IO.Directory.CreateDirectory(stagingParent);
            using var parent = WindowsProjectIdentity.Open(stagingParent);
            var candidate = Path.Combine(stagingParent, ".modpacktools-native-" + Guid.NewGuid().ToString("N"));
            if (!WindowsDirectoryReservation.TryCreate(candidate)) throw Error("StageCreateFailed", "Native package stage could not be reserved", candidate);
            stage = candidate;
            using var identity = WindowsProjectIdentity.Open(stage); stageIdentity = identity.Value;
            long completed = 0; var count = 0;
            foreach (var file in manifest.Files)
            {
                cancellation.ThrowIfCancellationRequested(); identity.AssertCurrent(); parent.AssertCurrent();
                var path = TransactionFiles.Within(stage, file.Path);
                CreateDirectories(stage, file.Path, directories);
                owned.Add(file.Path, null);
                string hash;
                using (var source = entries[file.Path].Open())
                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    hash = Copy(source, destination, file.Length, cancellation,
                        bytes => progress?.Invoke(new("extract", path, completed + bytes, total, count, manifest.Files.Length + 1)));
                    destination.Flush(flushToDisk: true);
                }
                owned[file.Path] = file with { Sha256 = hash };
                if (hash != file.Sha256) throw Error("PayloadHashMismatch", "Native payload failed SHA-256 verification", path);
                if (file.Path.EndsWith(".exe", StringComparison.Ordinal)) VerifyNativeExecutable(path);
                completed += file.Length; count++;
                progress?.Invoke(new("extracted", path, completed, total, count, manifest.Files.Length + 1));
            }
            var manifestPath = Path.Combine(stage, "package.json"); owned.Add("package.json", null);
            using (var destination = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { destination.Write(canonical); destination.Flush(flushToDisk: true); }
            owned["package.json"] = new("package.json", canonical.Length, Convert.ToHexString(SHA256.HashData(canonical)));
            var result = new NativePreparedPackage(stage, identity.Value, manifest,
                owned.Values.Select(file => file!).OrderBy(file => file.Path, StringComparer.Ordinal).ToImmutableArray(), archiveDigest);
            Verify(result, limits, cancellation, progress);
            parent.AssertCurrent(); identity.AssertCurrent(); cancellation.ThrowIfCancellationRequested(); return result;
        }
        catch (Exception error)
        {
            if (stage is not null)
            {
                try { Cleanup(stage, stageIdentity, owned, directories, limits); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
                {
                    var retained = Error("StageRetained", "Native package preparation failed; inspect its retained stage", stage, error.Message + "; " + cleanup.Message);
                    retained.Data["ModpackTools.RetainStage"] = true; throw retained;
                }
            }
            if (error is OperationCanceledException) throw new DomainException("Core.OperationCancelled", "Native package preparation was cancelled", "retry when ready", "OperationStopped", archivePath);
            if (error is ArchiveLimitException) throw Error("ArchiveLimit", error.Message, archivePath);
            if (error is InvalidDataException or BadImageFormatException) throw Error("ArchiveInvalid", "Native package data could not be read", archivePath, error.Message);
            throw;
        }
    }

    public static void Verify(NativePreparedPackage prepared, NativePackageLimits? limits = null,
        CancellationToken cancellation = default, Action<NativePackageProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(prepared); limits ??= new(); ValidateLimits(limits); prepared.Manifest.Validate();
        if (!NativePackageManifest.HashValue(prepared.ArchiveSha256)) throw Error("InvalidArchiveHash", "Prepared archive identity is invalid", prepared.Directory);
        var canonical = prepared.Manifest.ToBytes();
        var expected = prepared.Manifest.Files.Add(new("package.json", canonical.Length, Convert.ToHexString(SHA256.HashData(canonical))))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToImmutableArray();
        if (prepared.Files.IsDefault || !prepared.Files.SequenceEqual(expected)) throw Error("InvalidPreparedPackage", "Prepared package does not match its manifest", prepared.Directory);
        using var identity = WindowsProjectIdentity.Open(prepared.Directory);
        if (identity.Value != prepared.Identity) throw Error("StageChanged", "Prepared package directory identity changed", prepared.Directory);
        var known = expected.ToDictionary(file => file.Path, file => (NativePackageFile?)file, StringComparer.Ordinal);
        var directories = Ancestors(expected.Select(file => file.Path));
        VerifyTree(prepared.Directory, known, directories, limits, cancellation, progress);
        foreach (var file in prepared.Manifest.Files.Where(file => file.Path.EndsWith(".exe", StringComparison.Ordinal)))
            VerifyNativeExecutable(TransactionFiles.Within(prepared.Directory, file.Path));
        identity.AssertCurrent(); cancellation.ThrowIfCancellationRequested();
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateArchive(ZipArchive archive, NativePackageLimits limits)
    {
        if (archive.Entries.Count > limits.MaximumEntries) throw Error("ArchiveLimit", "Native archive has too many entries");
        if (archive.Entries.Any(entry => entry.FullName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith("ModpackTools.psd1", StringComparison.OrdinalIgnoreCase)))
            throw Error("LegacyArchive", "PowerShell release archives cannot install or update the native application");
        var allowed = NativePackageManifest.PayloadPaths.Add("package.json").ToHashSet(StringComparer.Ordinal);
        var ancestors = Ancestors(allowed); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal); long total = 0;
        foreach (var entry in archive.Entries)
        {
            var directory = entry.FullName.EndsWith('/'); var name = directory ? entry.FullName[..^1] : entry.FullName;
            if (entry.FullName.Contains('\\') || !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || !(directory ? ancestors.Contains(name) : allowed.Contains(name)))
                throw Error("ArchiveInvalid", "Native archive contains an unknown, unsafe or duplicate path", entry.FullName);
            if (entry.Length < 0 || entry.Length > limits.MaximumExpandedBytes - total || entry.Length > Limit(name, limits))
                throw Error("ArchiveLimit", "Native archive entry exceeds its byte quota", entry.FullName);
            total += entry.Length;
            if (directory) { if (entry.Length != 0) throw Error("ArchiveInvalid", "Archive directory contains data", entry.FullName); }
            else { if (entry.Length == 0) throw Error("ArchiveIncomplete", "Native payload must not be empty", name); entries.Add(name, entry); }
        }
        if (entries.Count != allowed.Count) throw Error("ArchiveIncomplete", "Native archive is missing required payload files");
        return entries;
    }

    private static long Limit(string name, NativePackageLimits limits) => name == "package.json" ? limits.MaximumManifestBytes
        : name.EndsWith(".exe", StringComparison.Ordinal) ? limits.MaximumExecutableBytes
        : name.EndsWith(".nu", StringComparison.Ordinal) ? limits.MaximumNuBytes
        : name.EndsWith(".json", StringComparison.Ordinal) ? limits.MaximumJsonBytes : limits.MaximumTextBytes;

    private static void VerifyNativeExecutable(string path)
    {
        WindowsProjectIdentity.GuardAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var image = new PEReader(input);
        var headers = image.PEHeaders;
        if (headers.CoffHeader.Machine != Machine.Amd64 || headers.PEHeader?.Magic != PEMagic.PE32Plus || headers.CorHeader is not null
            || (headers.CoffHeader.Characteristics & Characteristics.ExecutableImage) == 0
            || (headers.CoffHeader.Characteristics & Characteristics.Dll) != 0)
            throw Error("NotNativeExecutable", "Package executables must be native Windows x64 applications without CLR headers", path);
    }

    private static void VerifyTree(string stage, Dictionary<string, NativePackageFile?> files, HashSet<string> directories,
        NativePackageLimits limits, CancellationToken cancellation, Action<NativePackageProgress>? progress = null)
    {
        var pending = new Stack<string>(); pending.Push(stage); var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal); long completed = 0; var total = files.Values.Sum(file => file?.Length ?? 0);
        while (pending.TryPop(out var directory))
        {
            using var directoryIdentity = WindowsProjectIdentity.Open(directory);
            directoryIdentity.AssertDefaultStreamOnly();
            foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(directory))
            {
                cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(path);
                var relative = Path.GetRelativePath(stage, path).Replace('\\', '/');
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                {
                    if (!directories.Contains(relative) || !seenDirectories.Add(relative)) throw Error("StageChanged", "Package stage contains an unknown directory", path);
                    pending.Push(path); continue;
                }
                if (!files.TryGetValue(relative, out var file) || file is null || !seenFiles.Add(relative)) throw Error("StageChanged", "Package stage contains unknown or incomplete contents", path);
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
                WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, Limit(relative, limits), path, defaultStreamOnly: true);
                if (input.Length != file.Length || file.Length > Limit(relative, limits) || file.Length > limits.MaximumExpandedBytes - completed)
                    throw Error("StageChanged", "Package stage file length changed or exceeds its quota", path);
                if (Copy(input, Stream.Null, file.Length, cancellation) != file.Sha256) throw Error("StageChanged", "Package stage file hash changed", path);
                completed += file.Length;
                progress?.Invoke(new("verify", path, completed, total, seenFiles.Count, files.Count));
            }
            directoryIdentity.AssertCurrent();
        }
        if (seenFiles.Count != files.Count || seenDirectories.Count != directories.Count) throw Error("StageChanged", "Package stage contents are incomplete", stage);
    }

    internal static void Cleanup(string stage, ProjectPhysicalIdentity? expected, Dictionary<string, NativePackageFile?> files,
        HashSet<string> directories, NativePackageLimits limits)
    {
        using (var identity = WindowsProjectIdentity.Open(stage))
        {
            if (expected is null || identity.Value != expected) throw Error("StageChanged", "Package stage identity cannot be verified for cleanup", stage);
            VerifyTree(stage, files, directories, limits, CancellationToken.None); identity.AssertCurrent();
            foreach (var file in files.Keys) { var path = TransactionFiles.Within(stage, file); WindowsProjectIdentity.GuardAncestors(path); File.Delete(path); }
            foreach (var relative in directories.OrderByDescending(value => value.Length))
            { var path = TransactionFiles.Within(stage, relative); WindowsProjectIdentity.GuardAncestors(path); System.IO.Directory.Delete(path, recursive: false); }
        }
        System.IO.Directory.Delete(stage, recursive: false);
    }

    internal static void CreateDirectories(string stage, string file, HashSet<string> created)
    {
        foreach (var relative in Ancestors([file]).OrderBy(value => value.Length))
        {
            if (created.Contains(relative)) continue;
            var path = TransactionFiles.Within(stage, relative);
            if (!WindowsDirectoryReservation.TryCreate(path)) throw Error("StageChanged", "Native package directory could not be exclusively reserved", path);
            created.Add(relative);
        }
    }

    private static HashSet<string> Ancestors(IEnumerable<string> files)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
            for (var slash = file.IndexOf('/'); slash >= 0; slash = file.IndexOf('/', slash + 1)) result.Add(file[..slash]);
        return result;
    }

    internal static string Copy(Stream source, Stream destination, long length, CancellationToken cancellation, Action<long>? progress = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(65536); long total = 0;
        try
        {
            int read;
            while ((read = source.Read(buffer, 0, 65536)) != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (read > length - total) throw Error("ArchiveLimit", "Native package data exceeds its declared byte quota");
                destination.Write(buffer, 0, read); hash.AppendData(buffer.AsSpan(0, read)); total += read; progress?.Invoke(total);
            }
            cancellation.ThrowIfCancellationRequested();
            if (total != length) throw Error("EntryLengthMismatch", "Native package data length differs from its declaration");
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static void ValidateLimits(NativePackageLimits limits)
    {
        if (limits.MaximumArchiveBytes <= 0 || limits.MaximumExpandedBytes <= 0 || limits.MaximumEntries <= 0 || limits.MaximumManifestBytes <= 0
            || limits.MaximumExecutableBytes <= 0 || limits.MaximumNuBytes <= 0 || limits.MaximumJsonBytes <= 0 || limits.MaximumTextBytes <= 0)
            throw Error("InvalidPolicy", "Native package quotas must be positive");
    }

    private static DomainException Error(string id, string message, string? path = null, string? details = null) => NativePackageManifest.Error(id, message, path, details);
}
