using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ModpackTools.Core.Data;
using ModpackTools.Core.Source;

namespace ModpackTools.Core.Providers;

public enum ProviderCacheStatus { Fresh, Missing, Expired, Empty, Unreadable }
public sealed record ProviderPublication(string Text, DateTime? Value, DateTimeOffset? Instant);
public sealed record ProviderFile(string Filename, string Url, bool Primary, string Sha1, string Sha512, long? Size);
public sealed record ProviderDependency(string ProjectId, string VersionId, string Type, string FileName);
public sealed record ProviderVersion(string Id, string ProjectId, string Name, string VersionNumber,
    ProviderPublication Published, ImmutableArray<ProviderFile> Files, ImmutableArray<ProviderDependency> Dependencies,
    ImmutableArray<string> Loaders, ImmutableArray<string> GameVersions);
public sealed record ProviderCacheObservation(ProviderCacheStatus Status, string Path,
    DateTime? ModifiedUtc, ProviderVersion? Version, string? Diagnostic);
public sealed record ProviderCacheReadLimits(long MaximumBytes = 16 * 1024 * 1024, int MaximumDepth = 64,
    int MaximumNormalizedCharacters = 16 * 1024 * 1024, int MaximumTokenCharacters = 4 * 1024 * 1024,
    int MaximumNodes = 100000, int MaximumCollectionEntries = 100000);

public static class ProviderVersionCache
{
    public static ProviderCacheObservation Read(string configurationDirectory, string versionId,
        DateTimeOffset now, ProviderCacheReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(configurationDirectory); ArgumentNullException.ThrowIfNull(versionId);
        limits ??= new();
        Validate(limits);
        var path = Path.Combine(Path.GetFullPath(configurationDirectory), "metadata", TextHash.Compute("version/" + versionId) + ".json");
        DateTime? modified = null;
        try
        {
            FileStream file;
            try { file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return Result(ProviderCacheStatus.Missing); }
            catch (UnauthorizedAccessException) when (Directory.Exists(path)) { return Result(ProviderCacheStatus.Missing); }
            using (file)
            {
                modified = File.GetLastWriteTimeUtc(file.SafeFileHandle);
                if (modified.Value.Ticks <= now.UtcDateTime.Ticks - TimeSpan.TicksPerDay) return Result(ProviderCacheStatus.Expired);
                var decoded = ReadDocument(file, limits);
                if (decoded is null) return Result(ProviderCacheStatus.Empty);
                using var document = decoded.Document;
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0) return Result(ProviderCacheStatus.Empty);
                    if (root.GetArrayLength() != 1) throw new FormatException("A provider version must contain one object.");
                    root = root[0];
                }
                if (root.ValueKind == JsonValueKind.Null) return Result(ProviderCacheStatus.Empty);
                JsonDisplayProjection.Validate(root);
                var version = Decode(root, decoded.Dates, limits.MaximumCollectionEntries);
                return new(ProviderCacheStatus.Fresh, path, modified, version, null);
            }
        }
        catch (CacheJsonLimitException exception) { throw Limit(path, exception.Message); }
        catch (Newtonsoft.Json.JsonReaderException exception) when (exception.Message.StartsWith("The reader's MaxDepth of ", StringComparison.Ordinal))
        { throw Limit(path, exception.Message); }
        catch (JsonException exception) when (exception.Message.StartsWith("The maximum configured depth of ", StringComparison.Ordinal))
        { throw Limit(path, exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException
            or JsonException or Newtonsoft.Json.JsonException or InvalidOperationException or InvalidCastException or OverflowException)
        { return Result(ProviderCacheStatus.Unreadable, exception.Message); }
        ProviderCacheObservation Result(ProviderCacheStatus status, string? diagnostic = null) => new(status, path, modified, null, diagnostic);
    }

    internal static void Validate(ProviderCacheReadLimits limits)
    {
        var invalid = limits.MaximumBytes <= 0 ? nameof(limits.MaximumBytes)
            : limits.MaximumDepth is < 1 or > 1024 ? nameof(limits.MaximumDepth)
            : limits.MaximumNormalizedCharacters <= 0 ? nameof(limits.MaximumNormalizedCharacters)
            : limits.MaximumTokenCharacters <= 0 ? nameof(limits.MaximumTokenCharacters)
            : limits.MaximumNodes <= 0 ? nameof(limits.MaximumNodes)
            : limits.MaximumCollectionEntries <= 0 ? nameof(limits.MaximumCollectionEntries) : null;
        if (invalid is not null) throw new DomainException("Metadata.InvalidProviderCachePolicy", "Provider cache limits are invalid",
            "use positive quotas and a depth between 1 and 1024", target: invalid);
    }

    internal static DecodedCache? ReadDocument(Stream source, ProviderCacheReadLimits limits)
    {
        using var bounded = new ProviderInput(source, limits.MaximumBytes);
        using var reader = new StreamReader(bounded, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return CacheTokenCodec.Read(reader, limits.MaximumDepth, limits.MaximumNormalizedCharacters,
            limits.MaximumTokenCharacters, limits.MaximumNodes);
    }

    internal static ProviderVersion Decode(JsonElement root, CacheDateTable dates, int maximumEntries)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("A provider version must be an object.");
        var count = 0;
        var id = Scalar(root, "id", required: true);
        var publication = Field(root, "date_published");
        var publishedText = Scalar(root, "date_published");
        var captured = publication.ValueKind == JsonValueKind.String && dates.TryGet(JsonDisplayProjection.DecodedString(publication), out var date)
            ? (CacheDateValue?)date : null;
        var files = ImmutableArray.CreateBuilder<ProviderFile>();
        foreach (var file in Collection(root, "files"))
        {
            var primary = Field(file, "primary");
            if (primary.ValueKind is JsonValueKind.Object or JsonValueKind.Array || primary.ValueKind == JsonValueKind.String
                && dates.TryGet(JsonDisplayProjection.DecodedString(primary), out _)) throw new FormatException("The primary flag must be a scalar Boolean-compatible value.");
            var isPrimary = primary.ValueKind == JsonValueKind.True || primary.ValueKind == JsonValueKind.Number && primary.GetDouble() == 1
                || primary.ValueKind == JsonValueKind.String && StringComparer.InvariantCultureIgnoreCase.Equals(Scalar(file, "primary"), "True");
            var hashes = Field(file, "hashes");
            if (hashes.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object)) throw new FormatException("File hashes must be an object.");
            var size = Field(file, "size");
            long? declaredSize = null;
            if (size.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                if (size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var parsedSize)) throw new FormatException("Declared file size must be an Int64 integer.");
                declaredSize = parsedSize;
            }
            files.Add(new(Scalar(file, "filename"), Scalar(file, "url"), isPrimary,
                hashes.ValueKind == JsonValueKind.Object ? Scalar(hashes, "sha1") : "",
                hashes.ValueKind == JsonValueKind.Object ? Scalar(hashes, "sha512") : "", declaredSize));
        }
        var dependencies = ImmutableArray.CreateBuilder<ProviderDependency>();
        foreach (var dependency in Collection(root, "dependencies"))
            dependencies.Add(new(Scalar(dependency, "project_id"), Scalar(dependency, "version_id"),
                Scalar(dependency, "dependency_type"), Scalar(dependency, "file_name")));
        return new(id, Scalar(root, "project_id"), Scalar(root, "name"), Scalar(root, "version_number"),
            new(publishedText, captured?.Value, captured?.Instant), files.ToImmutable(), dependencies.ToImmutable(),
            Strings("loaders"), Strings("game_versions"));

        JsonElement Field(JsonElement value, string name)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Provider collection items must be objects.");
            foreach (var (key, field) in JsonDisplayProjection.Properties(value))
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) return field;
            return default;
        }
        string Scalar(JsonElement value, string name, bool required = false)
        {
            var field = Field(value, name);
            if (field.ValueKind == JsonValueKind.Undefined)
            { if (required) throw new FormatException("Provider version ID is missing."); return ""; }
            if (field.ValueKind is JsonValueKind.Object or JsonValueKind.Array) throw new FormatException("Provider identity/text fields must be scalars.");
            return CacheJson.String(new(field), dates);
        }
        IEnumerable<JsonElement> Collection(JsonElement value, string name)
        {
            var field = Field(value, name);
            if (field.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) yield break;
            if (field.ValueKind != JsonValueKind.Array) throw new FormatException("Provider collections must be arrays.");
            foreach (var item in field.EnumerateArray())
            {
                if (count >= maximumEntries) throw Limit("MaximumCollectionEntries", "Provider collections exceed their entry quota.");
                count++; yield return item;
            }
        }
        ImmutableArray<string> Strings(string name)
        {
            var result = ImmutableArray.CreateBuilder<string>();
            foreach (var item in Collection(root, name))
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array) throw new FormatException("Provider string collections must contain scalars.");
                result.Add(CacheJson.String(new(item), dates));
            }
            return result.ToImmutable();
        }
    }
    private static DomainException Limit(string target, string detail) => new("Metadata.ProviderCacheLimit", "Provider metadata cache exceeds its resource limit",
        "reduce metadata input or use explicit bounded quotas", "LimitsExceeded", target, details: detail);
    private sealed class ProviderInput(Stream source, long maximumBytes) : Stream
    {
        private long read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = source.Read(buffer);
            if (count > maximumBytes - read) throw new CacheJsonLimitException("Actual provider cache bytes exceed the quota.");
            read += count; return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
