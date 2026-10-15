using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Build;

internal static class MrpackInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-mrpack <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 }); var input = document.RootElement;
            if (input.TryGetProperty("Culture", out var culture)) CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture.GetString()!);
            var limits = new MrpackReadLimits();
            if (input.TryGetProperty("Limits", out var policy)) limits = new(new ArchiveReadLimits(MaximumCentralDirectoryBytes: Long("MaximumCentralDirectoryBytes", 64 * 1024 * 1024), MaximumEntries: Number("MaximumEntries", 100000)),
                Long("MaximumExpandedBytes", limits.MaximumExpandedBytes), Number("MaximumManifestBytes", limits.MaximumManifestBytes), Number("MaximumRecords", limits.MaximumRecords), Number("MaximumTextCharacters", limits.MaximumTextCharacters));
            using var cancellation = new CancellationTokenSource(); if (input.TryGetProperty("Cancelled", out var cancel) && cancel.GetBoolean()) cancellation.Cancel();
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                if (input.TryGetProperty("Path", out var path)) Records(writer, "Snapshot", MrpackSnapshot.Read(path.GetString()!, limits, cancellation.Token));
                else
                {
                    var baseline = MrpackSnapshot.Read(input.GetProperty("Baseline").GetString()!, limits, cancellation.Token);
                    var current = MrpackSnapshot.Read(input.GetProperty("Current").GetString()!, limits, cancellation.Token);
                    var diff = MrpackSnapshot.Compare(baseline, current, cancellation: cancellation.Token);
                    Records(writer, "Baseline", baseline); Records(writer, "Current", current);
                    writer.WriteStartObject("Diff"); writer.WriteNumber("Total", diff.Total); Records(writer, "Added", diff.Added); Records(writer, "Removed", diff.Removed);
                    writer.WriteStartArray("Changed"); foreach (var item in diff.Changed)
                    { writer.WriteStartObject(); writer.WriteString("Key", item.Key); writer.WriteString("Kind", item.Kind); writer.WriteString("Path", item.Path); writer.WriteString("Before", item.Before); writer.WriteString("After", item.After); writer.WriteEndObject(); }
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
            int Number(string name, int fallback) => policy.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
            long Long(string name, long fallback) => policy.TryGetProperty(name, out var value) ? value.GetInt64() : fallback;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check MRPack fixture input", null); return 2; }
    }
    private static void Records(Utf8JsonWriter writer, string name, ImmutableArray<MrpackRecord> items)
    {
        writer.WriteStartArray(name); foreach (var item in items)
        { writer.WriteStartObject(); writer.WriteString("Key", item.Key); writer.WriteString("Kind", item.Kind); writer.WriteString("Path", item.Path); writer.WriteString("Fingerprint", item.Fingerprint); writer.WriteEndObject(); }
        writer.WriteEndArray();
    }
}
