using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

public sealed record NativeNuProcess(int Id, long StartedUtcTicks);
public sealed record NativeNuObservation(bool Complete, ImmutableArray<NativeNuProcess> Processes, string? Reason);
internal sealed record NativeRetirementObservation(int SchemaVersion, string InstallId, string RootIdentity,
    string Generation, NativeNuObservation Observation);

// Called only while the installation admission is held, after public bootstrap publication.
internal static class NativeRetirementCensus
{
    internal static NativeNuObservation Observe(string executable)
    {
        var result = ImmutableArray.CreateBuilder<NativeNuProcess>();
        var names = new[] { "nu", Path.GetFileNameWithoutExtension(executable) }.Distinct(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var name in names)
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Length > 4096) return new(false, result.ToImmutable(), "Nushell process census exceeds its quota");
                    foreach (var process in processes)
                    {
                        try { result.Add(new(process.Id, process.StartTime.ToUniversalTime().Ticks)); }
                        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                        { return new(false, result.ToImmutable(), "Cannot establish Nushell process identity: " + error.Message); }
                    }
                }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            return new(true, result.Distinct().OrderBy(value => value.Id).ToImmutableArray(), null);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return new(false, result.ToImmutable(), "Cannot enumerate Nushell processes: " + error.Message); }
    }

    internal static TransactionDiagnostic? Capture(NativeInstallationOwner owner, NativeControlGeneration control)
    {
        var path = Path.Combine(owner.Root.CanonicalRoot, "pins", "retirement-census.json");
        try
        {
            var observation = Observe(owner.NuExecutable);
            var value = new NativeRetirementObservation(1, owner.InstallId, owner.Root.FileIdentity, control.Generation, observation);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, NativeRetirementJson.Default.NativeRetirementObservation);
            byte[]? previous = null;
            if (File.Exists(path))
            {
                previous = ReadOwned(path, 128 * 1024); var existing = Parse(previous);
                if (existing.InstallId != owner.InstallId || existing.RootIdentity != owner.Root.FileIdentity)
                    throw new IOException("Retirement census owner changed: " + path);
            }
            WriteOwned(path, bytes, previous);
            return observation.Complete ? null : new("Installation.RetirementCensusPending", path, observation.Reason!);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DomainException or ArgumentException or JsonException or InvalidOperationException or FormatException)
        { return new("Installation.RetirementCensusPending", error is DomainException { Target: TextTarget target } ? target.Value : path, error.Message); }
    }

    internal static string? DeferredReason(NativeInstallationOwner owner, NativeControlGeneration control)
    {
        var current = Observe(owner.NuExecutable);
        if (!current.Complete) return current.Reason ?? "Current Nushell census is uncertain";
        var path = Path.Combine(owner.Root.CanonicalRoot, "pins", "retirement-census.json");
        try
        {
            if (!File.Exists(path)) return current.Processes.IsEmpty ? null : "No publication census; close existing Nushell sessions and retry";
            var bytes = ReadOwned(path, 128 * 1024);
            var previous = Parse(bytes);
            if (previous.SchemaVersion != 1 || previous.InstallId != owner.InstallId || previous.RootIdentity != owner.Root.FileIdentity
                || previous.Generation != control.Generation || previous.Observation is null || !previous.Observation.Complete)
                return current.Processes.IsEmpty ? null : "Publication census is unavailable for this generation; close existing Nushell sessions and retry";
            if (previous.Observation.Processes.IsDefault || previous.Observation.Processes.Length > 8192
                || previous.Observation.Processes.Any(value => value is null || value.Id <= 0 || value.StartedUtcTicks <= 0))
                return "Retirement census contains invalid process identities";
            return current.Processes.Intersect(previous.Observation.Processes).Any()
                ? "A Nushell session present at publication may still hold the retired module" : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DomainException or JsonException or InvalidOperationException or ArgumentException)
        { return "Retirement census cannot be verified: " + error.Message; }
    }

    internal static byte[] ReadOwned(string path, int maximum)
    {
        WindowsProjectIdentity.GuardAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, maximum, path, defaultStreamOnly: true);
        if (input.Length > maximum) throw new IOException("Managed metadata exceeds its byte quota: " + path);
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes); return bytes;
    }

    private static NativeRetirementObservation Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 });
        Fields(document.RootElement, "schema_version", "install_id", "root_identity", "generation", "observation");
        var observed = document.RootElement.GetProperty("observation"); Fields(observed, "complete", "processes", "reason");
        foreach (var process in observed.GetProperty("processes").EnumerateArray()) Fields(process, "id", "started_utc_ticks");
        var result = JsonSerializer.Deserialize(bytes, NativeRetirementJson.Default.NativeRetirementObservation) ?? throw new JsonException("Missing retirement census");
        if (result.SchemaVersion != 1 || !Guid.TryParseExact(result.InstallId, "N", out _) || !Guid.TryParseExact(result.Generation, "N", out _)
            || result.RootIdentity is not { Length: 48 } || !result.RootIdentity.All(Uri.IsHexDigit) || result.Observation is null
            || result.Observation.Processes.IsDefault || result.Observation.Processes.Length > 8192
            || result.Observation.Processes.Any(value => value is null || value.Id <= 0 || value.StartedUtcTicks <= 0)) throw new JsonException("Invalid retirement census identity");
        return result;
    }

    internal static void WriteOwned(string path, byte[] bytes, byte[]? previous)
    {
        if (bytes.Length > 128 * 1024) throw new IOException("Managed metadata exceeds its readable quota: " + path);
        using var parent = WindowsProjectIdentity.Open(Path.GetDirectoryName(path)!); parent.AssertDefaultStreamOnly();
        if (previous is not null && !ReadOwned(path, 128 * 1024).AsSpan().SequenceEqual(previous)) throw new IOException("Managed metadata changed: " + path);
        var temporary = Path.Combine(parent.Value.CanonicalRoot, ".census-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes); output.Flush(flushToDisk: true); }
            parent.AssertCurrent();
            if (previous is not null && !ReadOwned(path, 128 * 1024).AsSpan().SequenceEqual(previous)) throw new IOException("Managed metadata changed");
            File.Move(temporary, path, overwrite: previous is not null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DomainException)
        {
            var retained = NativeInstallationRoot.Error("RetirementMetadataRetained", "Preserve the retirement metadata temporary: " + temporary, temporary, error.Message);
            retained.Data["ModpackTools.RetainStage"] = true; throw retained;
        }
    }

    internal static void Fields(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Managed metadata field must be an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!expected.Contains(field.Name, StringComparer.Ordinal) || !names.Add(field.Name)) throw new JsonException("Unknown or duplicate managed metadata field: " + field.Name);
        if (names.Count != expected.Length) throw new JsonException("Missing managed metadata field");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(NativeRetirementObservation))]
internal sealed partial class NativeRetirementJson : JsonSerializerContext;
