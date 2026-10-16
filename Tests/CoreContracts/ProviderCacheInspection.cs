using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Providers;

internal static class ProviderCacheInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length < 4 || args.Length % 2 != 0) throw new ArgumentException("Use --inspect-provider-version-cache <config> <id> <now-iso> [--quota n].");
            var limits = new ProviderCacheReadLimits();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 4; i < args.Length; i += 2)
            {
                if (!seen.Add(args[i])) throw new ArgumentException("Duplicate provider cache option.");
                var value = long.Parse(args[i + 1], CultureInfo.InvariantCulture);
                limits = args[i] switch
                {
                    "--max-cache-bytes" => limits with { MaximumBytes = value },
                    "--max-cache-depth" => limits with { MaximumDepth = checked((int)value) },
                    "--max-cache-normalized-chars" => limits with { MaximumNormalizedCharacters = checked((int)value) },
                    "--max-cache-token-chars" => limits with { MaximumTokenCharacters = checked((int)value) },
                    "--max-cache-nodes" => limits with { MaximumNodes = checked((int)value) },
                    "--max-cache-collection-entries" => limits with { MaximumCollectionEntries = checked((int)value) },
                    _ => throw new ArgumentException("Unsupported provider cache option.")
                };
            }
            var now = DateTimeOffset.Parse(args[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var result = ProviderVersionCache.Read(args[1], args[2], now, limits);
            using var output = new BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteString("Status", result.Status.ToString()); writer.WriteString("Path", result.Path);
                writer.WriteString("ModifiedUtc", result.ModifiedUtc?.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteString("Diagnostic", result.Diagnostic); writer.WritePropertyName("Version");
                if (result.Version is not { } version) writer.WriteNullValue();
                else
                {
                    WriteVersion(writer, version);
                }
                writer.WriteEndObject();

            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, (int)output.Length); return 0;
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check provider cache inspection arguments", null); return 2; }
    }
    internal static void WriteVersion(Utf8JsonWriter writer, ProviderVersion version)
    {
        writer.WriteStartObject(); writer.WriteString("Id", version.Id); writer.WriteString("ProjectId", version.ProjectId);
        writer.WriteString("Name", version.Name); writer.WriteString("VersionNumber", version.VersionNumber);
        writer.WriteStartObject("Published"); writer.WriteString("Text", version.Published.Text);
        writer.WriteString("Value", version.Published.Value?.ToString("o", CultureInfo.InvariantCulture));
        writer.WriteString("Kind", version.Published.Value?.Kind.ToString()); writer.WriteString("Instant", version.Published.Instant?.ToString("o", CultureInfo.InvariantCulture)); writer.WriteEndObject();
        writer.WriteStartArray("Files"); foreach (var file in version.Files)
        {
            writer.WriteStartObject(); writer.WriteString("Filename", file.Filename); writer.WriteString("Url", file.Url);
            writer.WriteBoolean("Primary", file.Primary); writer.WriteString("Sha1", file.Sha1); writer.WriteString("Sha512", file.Sha512);
            if (file.Size is { } size) writer.WriteNumber("Size", size); else writer.WriteNull("Size"); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteStartArray("Dependencies"); foreach (var dependency in version.Dependencies)
        {
            writer.WriteStartObject(); writer.WriteString("ProjectId", dependency.ProjectId); writer.WriteString("VersionId", dependency.VersionId);
            writer.WriteString("Type", dependency.Type); writer.WriteString("FileName", dependency.FileName); writer.WriteEndObject();
        }
        writer.WriteEndArray(); Strings("Loaders", version.Loaders); Strings("GameVersions", version.GameVersions); writer.WriteEndObject();
        void Strings(string name, IEnumerable<string> values)
        { writer.WriteStartArray(name); foreach (var value in values) writer.WriteStringValue(value); writer.WriteEndArray(); }
    }
    internal sealed class BoundedOutput : MemoryStream
    {
        private void Guard(int count)
        {
            if (count > 32 * 1024 * 1024 - Length) throw new DomainException("Metadata.ProviderInspectionLimit", "Provider inspection output exceeds32MiB",
                "inspect smaller version metadata", "LimitsExceeded", "MaximumOutputBytes");
        }
        public override void Write(byte[] buffer, int offset, int count) { Guard(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Guard(buffer.Length); base.Write(buffer); }
    }
}
