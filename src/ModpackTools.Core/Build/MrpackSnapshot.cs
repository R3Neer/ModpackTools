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
public sealed record MrpackComparison(ImmutableArray<MrpackRecord> Added, ImmutableArray<MrpackRecord> Removed, ImmutableArray<MrpackChange> Changed)
{
    public int Total => checked(Added.Length + Removed.Length + Changed.Length);
}

public static class MrpackSnapshot
{
    public static ImmutableArray<MrpackRecord> Read(string path, MrpackReadLimits? limits = null, CancellationToken cancellation = default)
    {
        limits ??= new(); var archiveLimits = limits.Archive ?? new(); archiveLimits.Validate();
        if (limits.MaximumExpandedBytes <= 0 || limits.MaximumManifestBytes is <= 0 or > 8 * 1024 * 1024 || limits.MaximumRecords <= 0 || limits.MaximumTextCharacters <= 0)
            throw new DomainException("Diff.InvalidReadPolicy", "MRPack reader quotas must be positive and bounded", "use explicit bounded MRPack quotas");
        if (!File.Exists(path)) throw new DomainException("Diff.ArtifactNotFound", $"MRPack file '{path}' does not exist", "modpack build", "ObjectNotFound", path);
        var records = ImmutableArray.CreateBuilder<MrpackRecord>(); var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0; long text = 0;
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
            foreach (var dependency in dependencies.EnumerateObject().OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value.Name, StringComparer.Ordinal))
                Add(new("dependency:" + dependency.Name, "DEPENDENCY", dependency.Name, String(dependency.Value)));
            var files = manifest.GetProperty("files"); if (files.ValueKind != JsonValueKind.Array) throw Invalid("MRPack files must be an array");
            foreach (var item in files.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested(); Object(item); var name = String(item.GetProperty("path")); Relative(name);
                var parts = new StringBuilder();
                Properties(item.GetProperty("hashes"), "hash:");
                var downloads = item.GetProperty("downloads"); if (downloads.ValueKind != JsonValueKind.Array) throw Invalid("MRPack downloads must be an array");
                foreach (var download in downloads.EnumerateArray().Select(String).Order(StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value, StringComparer.Ordinal)) Line("download=" + download);
                if (item.TryGetProperty("env", out var environment)) Properties(environment, "env:");
                if (!item.GetProperty("fileSize").TryGetInt64(out var size) || size < 0) throw Invalid("MRPack fileSize must be a nonnegative Int64");
                Line("size=" + size.ToString(CultureInfo.InvariantCulture));
                Add(new("manifest:" + name, Kind(name, false), name, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString()))).ToLowerInvariant()));
                void Properties(JsonElement value, string prefix)
                {
                    Object(value);
                    foreach (var property in value.EnumerateObject().OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value.Name, StringComparer.Ordinal))
                        Line(prefix + property.Name + "=" + String(property.Value));
                }
                void Line(string value) { Text(value); if (parts.Length != 0) parts.Append('\n'); parts.Append(value); }
            }
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested(); var name = entry.FullName;
                if (name == "modrinth.index.json" || name.EndsWith('/')) continue;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); ReadEntry(entry, chunk => hash.AppendData(chunk));
                var fingerprint = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (name.StartsWith("overrides/", StringComparison.Ordinal))
                { var relative = name[10..]; Add(new("override:" + relative, Kind(relative, true), relative, fingerprint)); }
                else Add(new("entry:" + name, "ARCHIVE", name, fingerprint));
            }
            cancellation.ThrowIfCancellationRequested(); return records.ToImmutable();
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
