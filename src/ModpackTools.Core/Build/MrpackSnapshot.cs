using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Build;

public sealed record MrpackReadLimits(ArchiveReadLimits? Archive = null, long MaximumExpandedBytes = 1L << 40,
    int MaximumManifestBytes = 8 * 1024 * 1024, int MaximumRecords = 200000, int MaximumTextCharacters = 64 * 1024 * 1024);
public sealed record MrpackRecord(string Key, string Kind, string Path, string Fingerprint);
public sealed record MrpackChange(string Key, string Kind, string Path, string Before, string After);
public sealed record MrpackManifestFile(string Path, ImmutableDictionary<string, string> Hashes, ImmutableArray<string> Downloads,
    ImmutableDictionary<string, string> Environment, long Size);
public sealed record MrpackArchiveFile(string Path, ImmutableDictionary<string, string> Hashes, long Size);
public sealed record MrpackDocument(string Name, string Version, ImmutableDictionary<string, string> Dependencies,
    ImmutableArray<MrpackManifestFile> Files, ImmutableArray<MrpackArchiveFile> Entries, ImmutableArray<MrpackRecord> Records);
public sealed record MrpackComparison(ImmutableArray<MrpackRecord> Added, ImmutableArray<MrpackRecord> Removed, ImmutableArray<MrpackChange> Changed)
{
    public int Total => checked(Added.Length + Removed.Length + Changed.Length);
}

public static class MrpackSnapshot
{
    public static ImmutableArray<MrpackRecord> Read(string path, MrpackReadLimits? limits = null, CancellationToken cancellation = default)
        => ReadCore(path, limits, cancellation, false).Records;
    public static MrpackDocument ReadDocument(string path, MrpackReadLimits? limits = null, CancellationToken cancellation = default)
        => ReadCore(path, limits, cancellation, true);
    private static MrpackDocument ReadCore(string path, MrpackReadLimits? limits, CancellationToken cancellation, bool hashes)
    {
        limits ??= new(); var archiveLimits = limits.Archive ?? new(); archiveLimits.Validate();
        if (limits.MaximumExpandedBytes <= 0 || limits.MaximumManifestBytes is <= 0 or > 8 * 1024 * 1024 || limits.MaximumRecords <= 0 || limits.MaximumTextCharacters <= 0)
            throw new DomainException("Diff.InvalidReadPolicy", "MRPack reader quotas must be positive and bounded", "use explicit bounded MRPack quotas");
        if (!File.Exists(path)) throw new DomainException("Diff.ArtifactNotFound", $"MRPack file '{path}' does not exist", "modpack build", "ObjectNotFound", path);
        var records = ImmutableArray.CreateBuilder<MrpackRecord>(); var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0; long text = 0;
        var manifestFiles = ImmutableArray.CreateBuilder<MrpackManifestFile>(); var archiveFiles = ImmutableArray.CreateBuilder<MrpackArchiveFile>();
        try
        {
            cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _ = ArchivePreflight.Validate(file, archiveLimits);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested(); var name = entry.FullName; var directory = name.EndsWith('/');
                Relative(directory ? name[..^1] : name); Text(name);
                if (!names.Add(directory ? name[..^1] : name)) throw Invalid("MRPack contains duplicate or case-colliding ZIP paths");
                if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || directory && entry.Length != 0) throw Invalid("MRPack contains a linked or nonempty directory entry");
            }
            var manifestEntry = archive.GetEntry("modrinth.index.json") ?? throw new DomainException("Diff.ManifestMissing", $"MRPack file '{path}' does not contain 'modrinth.index.json'", "modpack build", "InvalidData", path);
            using var manifestBytes = new MemoryStream(); ReadEntry(manifestEntry, chunk =>
            {
                if (chunk.Length > limits.MaximumManifestBytes - manifestBytes.Length) throw Limit("MaximumManifestBytes"); manifestBytes.Write(chunk);
            });
            manifestBytes.Position = 0;
            using var reader = new StreamReader(manifestBytes, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var manifestText = reader.ReadToEnd(); Text(manifestText);
            using var json = JsonDocument.Parse(manifestText, new() { MaxDepth = 64 });
            var manifest = json.RootElement; Object(manifest);
            foreach (var name in new[] { "name", "versionId" }) Add(new("pack:" + name, "PACK", name, String(manifest.GetProperty(name))));
            var dependencies = manifest.GetProperty("dependencies"); Object(dependencies);
            var dependencyValues = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var dependency in dependencies.EnumerateObject().OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value.Name, StringComparer.Ordinal))
            { var value = String(dependency.Value); dependencyValues.Add(dependency.Name, value); Add(new("dependency:" + dependency.Name, "DEPENDENCY", dependency.Name, value)); }
            var files = manifest.GetProperty("files"); if (files.ValueKind != JsonValueKind.Array) throw Invalid("MRPack files must be an array");
            foreach (var item in files.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested(); Object(item); var name = String(item.GetProperty("path")); Relative(name);
                var parts = new StringBuilder();
                var fileHashes = Properties(item.GetProperty("hashes"), "hash:");
                var downloads = item.GetProperty("downloads"); if (downloads.ValueKind != JsonValueKind.Array) throw Invalid("MRPack downloads must be an array");
                var downloadValues = downloads.EnumerateArray().Select(String).Order(StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value, StringComparer.Ordinal).ToImmutableArray();
                foreach (var download in downloadValues) Line("download=" + download);
                var environments = item.TryGetProperty("env", out var environment) ? Properties(environment, "env:") : ImmutableDictionary<string, string>.Empty;
                if (!item.GetProperty("fileSize").TryGetInt64(out var size) || size < 0) throw Invalid("MRPack fileSize must be a nonnegative Int64");
                Line("size=" + size.ToString(CultureInfo.InvariantCulture));
                manifestFiles.Add(new(name, fileHashes, downloadValues, environments, size));
                Add(new("manifest:" + name, Kind(name, false), name, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString()))).ToLowerInvariant()));
                ImmutableDictionary<string, string> Properties(JsonElement value, string prefix)
                {
                    Object(value); var values = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
                    foreach (var property in value.EnumerateObject().OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value.Name, StringComparer.Ordinal))
                    { var text = String(property.Value); values.Add(property.Name, text); Line(prefix + property.Name + "=" + text); }
                    return values.ToImmutable();
                }
                void Line(string value) { Text(value); if (parts.Length != 0) parts.Append('\n'); parts.Append(value); }
            }
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested(); var name = entry.FullName;
                if (name == "modrinth.index.json" || name.EndsWith('/')) continue;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var sha1 = hashes ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1) : null;
                using var sha512 = hashes ? IncrementalHash.CreateHash(HashAlgorithmName.SHA512) : null;
                using var md5 = hashes ? IncrementalHash.CreateHash(HashAlgorithmName.MD5) : null;
                ReadEntry(entry, chunk => { hash.AppendData(chunk); sha1?.AppendData(chunk); sha512?.AppendData(chunk); md5?.AppendData(chunk); });
                var fingerprint = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (hashes) archiveFiles.Add(new(name, ImmutableDictionary<string, string>.Empty.Add("sha256", fingerprint)
                    .Add("sha1", Convert.ToHexString(sha1!.GetHashAndReset())).Add("sha512", Convert.ToHexString(sha512!.GetHashAndReset())).Add("md5", Convert.ToHexString(md5!.GetHashAndReset())), entry.Length));
                if (name.StartsWith("overrides/", StringComparison.Ordinal))
                { var relative = name[10..]; Add(new("override:" + relative, Kind(relative, true), relative, fingerprint)); }
                else Add(new("entry:" + name, "ARCHIVE", name, fingerprint));
            }
            cancellation.ThrowIfCancellationRequested(); return new(String(manifest.GetProperty("name")), String(manifest.GetProperty("versionId")), dependencyValues.ToImmutable(),
                manifestFiles.ToImmutable(), archiveFiles.ToImmutable(), records.ToImmutable());
        }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "MRPack inspection was cancelled", "retry when ready", "OperationStopped"); }
        catch (ArchiveLimitException error) { throw Limit("Archive", error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        { throw Invalid("MRPack content cannot be read safely", error.Message); }
        void Text(string value)
        {
            if (value.Length > limits.MaximumTextCharacters - text) throw Limit("MaximumTextCharacters"); text += value.Length;
        }
        void Add(MrpackRecord record)
        {
            cancellation.ThrowIfCancellationRequested();
            if (records.Count >= limits.MaximumRecords) throw Limit("MaximumRecords");
            if (!keys.Add(record.Key)) throw Invalid("MRPack repeats a semantic record identity");
            Text(record.Key); Text(record.Path); Text(record.Kind); Text(record.Fingerprint); records.Add(record);
        }
        void ReadEntry(ZipArchiveEntry entry, Chunk consume)
        {
            if (entry.Length > limits.MaximumExpandedBytes - expanded) throw Limit("MaximumExpandedBytes");
            using var input = entry.Open(); var buffer = ArrayPool<byte>.Shared.Rent(65536); long actual = 0;
            try
            {
                int count; while ((count = input.Read(buffer, 0, 65536)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested(); if (count > limits.MaximumExpandedBytes - expanded) throw Limit("MaximumExpandedBytes");
                    expanded += count; actual += count; consume(buffer.AsSpan(0, count));
                }
                if (actual != entry.Length) throw Invalid("MRPack entry length differs from the actual expanded bytes");
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
        DomainException Invalid(string message, string? detail = null) => new("Diff.InvalidArtifact", message, "inspect the MRPack manifest and ZIP contents", "InvalidData", path, details: detail);
        void Object(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object) throw Invalid("MRPack manifest field must be an object");
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) if (!properties.Add(property.Name)) throw Invalid("MRPack JSON object repeats a property");
        }
        string String(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid("MRPack manifest field must be a string");
        void Relative(string value)
        {
            if (value.Length is 0 or > 4096 || value.IndexOfAny(['\\', ':', '<', '>', '"', '|', '?', '*']) >= 0 || value.Any(char.IsControl))
                throw Invalid("MRPack contains an unsafe relative path");
            var segments = value.Split('/');
            if (segments.Length > 64 || segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')))
                throw Invalid("MRPack contains an unsafe relative path");
        }
    }
    public static MrpackComparison Compare(ImmutableArray<MrpackRecord> baseline, ImmutableArray<MrpackRecord> current, int maximumRecords = 200000,
        int maximumTextCharacters = 128 * 1024 * 1024, CancellationToken cancellation = default)
    {
        if (maximumRecords <= 0 || maximumTextCharacters <= 0) throw new DomainException("Diff.InvalidReadPolicy", "MRPack comparison quotas must be positive", "use explicit bounded comparison quotas");
        if (baseline.IsDefault || current.IsDefault || baseline.Length > maximumRecords || current.Length > maximumRecords) throw Limit("MaximumRecords");
        long characters = 0;
        foreach (var record in baseline.Concat(current))
        {
            CheckCancellation();
            if (record is null || record.Key is null || record.Kind is null || record.Path is null || record.Fingerprint is null)
                throw new DomainException("Diff.InvalidSnapshot", "MRPack snapshot contains incomplete records", "read a complete bounded MRPack snapshot", "InvalidData");
            var length = (long)record.Key.Length + record.Kind.Length + record.Path.Length + record.Fingerprint.Length;
            if (length > maximumTextCharacters - characters) throw Limit("MaximumTextCharacters"); characters += length;
        }
        var old = Map(baseline); var next = Map(current);
        var added = Order(next.Values.Where(record => !old.ContainsKey(record.Key))).ToImmutableArray();
        var removed = Order(old.Values.Where(record => !next.ContainsKey(record.Key))).ToImmutableArray();
        var changed = next.Values.Where(record => old.TryGetValue(record.Key, out var before) && !StringComparer.InvariantCulture.Equals(before.Fingerprint, record.Fingerprint))
            .Select(record => new MrpackChange(record.Key, record.Kind, record.Path, old[record.Key].Fingerprint, record.Fingerprint))
            .OrderBy(record => record.Kind, StringComparer.CurrentCultureIgnoreCase).ThenBy(record => record.Path, StringComparer.CurrentCultureIgnoreCase).ThenBy(record => record.Key, StringComparer.Ordinal).ToImmutableArray();
        CheckCancellation(); return new(added, removed, changed);
        void CheckCancellation()
        {
            if (cancellation.IsCancellationRequested) throw new DomainException("Core.OperationCancelled", "MRPack comparison was cancelled", "retry when ready", "OperationStopped");
        }
        static IOrderedEnumerable<MrpackRecord> Order(IEnumerable<MrpackRecord> records) => records.OrderBy(record => record.Kind, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(record => record.Path, StringComparer.CurrentCultureIgnoreCase).ThenBy(record => record.Key, StringComparer.Ordinal);
        static Dictionary<string, MrpackRecord> Map(ImmutableArray<MrpackRecord> records)
        {
            var values = new Dictionary<string, MrpackRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
                if (record is null || record.Key is null || record.Kind is null || record.Path is null || record.Fingerprint is null || !values.TryAdd(record.Key, record))
                    throw new DomainException("Diff.InvalidSnapshot", "MRPack snapshot repeats an identity or contains incomplete records", "read a complete bounded MRPack snapshot", "InvalidData");
            return values;
        }
    }
    private delegate void Chunk(ReadOnlySpan<byte> bytes);
    private static string Kind(string path, bool overlay) => path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ? "MOD"
        : path.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase) ? "RESOURCE" : path.StartsWith("shaderpacks/", StringComparison.OrdinalIgnoreCase) ? "SHADER"
        : overlay && path.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ? "CONFIG" : overlay && path.StartsWith(".modpack/", StringComparison.OrdinalIgnoreCase) ? "METADATA" : overlay ? "OVERRIDE" : "FILE";
    private static DomainException Limit(string target, string? detail = null) => new("Diff.ReadLimitExceeded", "MRPack inspection exceeds its resource quota", "use explicit bounded archive quotas", "LimitsExceeded", target, details: detail);
}
