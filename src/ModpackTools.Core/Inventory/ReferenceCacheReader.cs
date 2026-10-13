using System.Text;
using System.Text.Json;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public sealed record ReferenceCacheReadLimits(long MaximumBytes = 64 * 1024 * 1024,
    int MaximumDepth = 1024, int MaximumRows = 1_000_000, int MaximumNormalizedCharacters = 64 * 1024 * 1024,
    int MaximumTokenCharacters = 4 * 1024 * 1024, int MaximumNodes = 1_000_000)
{
    internal void Validate()
    {
        var invalid = MaximumBytes <= 0 ? nameof(MaximumBytes)
            : MaximumDepth is < 1 or > 1024 ? nameof(MaximumDepth)
            : MaximumRows <= 0 ? nameof(MaximumRows)
            : MaximumNormalizedCharacters <= 0 ? nameof(MaximumNormalizedCharacters)
            : MaximumTokenCharacters <= 0 ? nameof(MaximumTokenCharacters)
            : MaximumNodes <= 0 ? nameof(MaximumNodes) : null;
        if (invalid is not null)
            throw new DomainException("Inventory.InvalidReferenceCacheReadPolicy", "Reference-cache reader limits are invalid",
                "use positive byte/row quotas and a JSON depth between 1 and 1024", target: invalid);
    }
}

public sealed class CachedInventorySnapshot
{
    private readonly Lazy<int> schema;
    private readonly Lazy<CacheRows?> results;
    private readonly Lazy<string> project;
    private readonly Lazy<JsonDisplayValue> created;
    internal CachedInventorySnapshot(JsonElement data, string path, ReferenceCacheReadLimits limits, CacheDateTable dates)
    {
        Data = data;
        Path = path;
        Dates = dates;
        schema = new(() => CacheFields.Access(path, () => CacheJson.Integer(CacheFields.Field(data, "SchemaVersion"), dates)));
        project = new(() => CacheFields.Access(path, () => CacheJson.String(CacheFields.Field(data, "ProjectId"), dates)));
        created = new(() => CacheFields.Access(path, () => CacheFields.Field(data, "CreatedUtc")));
        results = new(() => CacheFields.Access(path, () =>
        {
            var value = CacheFields.Field(data, "Results");
            if (value.Group is null && value.Scalar.ValueKind == JsonValueKind.Null) return null;
            var rows = new CacheRows(value, path, dates);
            if (rows.Count > limits.MaximumRows) throw ReferenceCacheReader.Limit(path, "Result occurrence count exceeds the reader quota.");
            return rows;
        }));
    }
    public string Path { get; }
    public JsonElement Data { get; }
    public void WriteData(Utf8JsonWriter writer) => Data.WriteTo(writer);
    internal CacheDateTable Dates { get; }
    internal bool HasValue => JsonDisplayProjection.Truthy(new(Data));
    internal int SchemaVersion => schema.Value;
    internal string ProjectId => project.Value;
    internal CacheRows? Results => results.Value;
    internal void CheckRowQuota()
    {
        try { _ = results.Value; }
        // Reading does not require header fields. Save a missing/member failure
        // for resolution, while enforcing a successfully projected row budget.
        catch (DomainException exception) when (exception.Id == "ModpackTools.Inventory.InvalidReferenceCache") { }
    }
    internal bool IsFresh(DateTimeOffset nowUtc) => CacheFields.Access(Path, () =>
        CacheJson.Timestamp(created.Value, Dates, out var timestamp) && (nowUtc - timestamp.ToUniversalTime()).TotalHours <= 24);
}

public sealed class CachedReference
{
    internal CachedReference(JsonElement data, string path, CacheDateTable dates)
    {
        // The legacy function returns through a pipeline: one array level is
        // enumerated after Kind/Source have been checked on the selected value.
        data = CacheFields.ReturnedData(data);
        Data = data.Clone();
        Path = path;
        this.dates = dates.ForRow(Data);
    }
    public JsonElement Data { get; }
    public string Path { get; }
    private readonly CacheDateTable dates;
    public string StringField(string name) => CacheFields.Access(Path, () => CacheJson.String(CacheFields.Field(Data, name), dates));
    public void WriteData(Utf8JsonWriter writer) => Data.WriteTo(writer);
}

internal readonly record struct CacheRowView(JsonElement Data, string Path, CacheDateTable Dates)
{
    public int Index
    {
        get { var data = Data; var dates = Dates; return CacheFields.Access(Path, () => CacheJson.Integer(CacheFields.Field(data, "Index"), dates)); }
    }
    public string Kind => String("Kind");
    public string Source => String("Source");
    private string String(string name)
    {
        var data = Data;
        var dates = Dates;
        return CacheFields.Access(Path, () => CacheJson.String(CacheFields.Field(data, name), dates));
    }
}

internal sealed class CacheRows(JsonDisplayValue value, string path, CacheDateTable dates)
{
    public int Count => value.Group?.Length ?? (value.Scalar.ValueKind == JsonValueKind.Array ? value.Scalar.GetArrayLength() : 1);
    public IEnumerable<CacheRowView> Enumerate()
    {
        if (value.Group is { } group) { foreach (var row in group) yield return new(row, path, dates); }
        else if (value.Scalar.ValueKind == JsonValueKind.Array) { foreach (var row in value.Scalar.EnumerateArray()) yield return new(row, path, dates); }
        else yield return new(value.Scalar, path, dates);
    }
}

internal static class CacheFields
{
    public static JsonElement ReturnedData(JsonElement data) =>
        data.ValueKind == JsonValueKind.Array && data.GetArrayLength() == 1 ? data[0] : data;

    public static JsonDisplayValue Field(JsonElement value, string name)
    {
        static JsonElement Direct(JsonElement item, string field)
        {
            if (item.ValueKind == JsonValueKind.Object)
                foreach (var (key, child) in JsonDisplayProjection.Properties(item))
                    if (string.Equals(key, field, StringComparison.OrdinalIgnoreCase)) return child;
            throw new FormatException($"Cache property '{field}' is unavailable.");
        }
        if (value.ValueKind != JsonValueKind.Array) return new(Direct(value, name));
        var projected = new List<JsonElement>();
        foreach (var item in value.EnumerateArray())
        {
            // Member enumeration is one level, without recursive array lookup.
            var field = Direct(item, name);
            if (field.ValueKind == JsonValueKind.Array) projected.AddRange(field.EnumerateArray());
            else projected.Add(field);
        }
        if (projected.Count == 0) throw new FormatException($"Cache property '{name}' is unavailable.");
        return projected.Count == 1 ? new(projected[0]) : new(default, projected.ToArray());
    }
    public static T Access<T>(string path, Func<T> access)
    {
        try { return access(); }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidOperationException or ArgumentException)
        {
            throw ReferenceCacheReader.Invalid(path, exception.Message);
        }
    }
}

public static class ReferenceCacheReader
{
    public static CachedInventorySnapshot? Read(string configurationDirectory, ReferenceCacheReadLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        var path = System.IO.Path.Combine(System.IO.Path.GetFullPath(configurationDirectory), "last-inventory.json");
        try
        {
            FileStream file;
            try { file = File.OpenRead(path); }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return null; }
            catch (UnauthorizedAccessException) when (Directory.Exists(path)) { return null; }
            using (file)
            using (var bounded = new CacheInput(file, limits.MaximumBytes))
            using (var reader = new StreamReader(bounded, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true))
            {
                var decoded = CacheTokenCodec.Read(reader, limits.MaximumDepth, limits.MaximumNormalizedCharacters,
                    limits.MaximumTokenCharacters, limits.MaximumNodes);
                if (decoded is null) return null;
                using var document = decoded.Document;
                JsonDisplayProjection.Validate(document.RootElement);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0) return null;
                    if (root.GetArrayLength() == 1) root = root[0];
                }
                if (root.ValueKind == JsonValueKind.Null) return null;
                var snapshot = new CachedInventorySnapshot(root.Clone(), path, limits, decoded.Dates);
                snapshot.CheckRowQuota();
                return snapshot;
            }
        }
        catch (JsonException exception) when (exception.Message.StartsWith("The maximum configured depth of ", StringComparison.Ordinal))
        { throw Limit(path, exception.Message); }
        catch (Newtonsoft.Json.JsonReaderException exception) when (exception.Message.StartsWith("The reader's MaxDepth of ", StringComparison.Ordinal))
        { throw Limit(path, exception.Message); }
        catch (CacheJsonLimitException exception) { throw Limit(path, exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or Newtonsoft.Json.JsonException or FormatException or OverflowException or InvalidOperationException or InvalidCastException or ArgumentException)
        { throw Invalid(path, null); }
    }
    internal static DomainException Invalid(string path, string? details) => new("Inventory.InvalidReferenceCache",
        $"Inventory reference cache '{path}' is invalid", "modpack content list", "InvalidData", path, details: details);
    internal static DomainException Limit(string path, string details) => new("Inventory.ReferenceCacheReadLimitExceeded",
        "Inventory reference cache exceeds the reader quota", "run modpack content list with narrower filters",
        "LimitsExceeded", path, details: details);

    private sealed class CacheInput(Stream source, long maximumBytes) : Stream
    {
        private long read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = source.Read(buffer);
            if (count > maximumBytes - read) throw new CacheJsonLimitException("Actual file bytes exceed the reader quota.");
            read += count;
            return count;
        }
    }
}
