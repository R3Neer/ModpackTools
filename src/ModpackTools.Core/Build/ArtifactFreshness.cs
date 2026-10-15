using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core.Data;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Build;

public sealed record ArtifactFreshnessLimits(SourceTreeReadLimits? Sources = null, BuildLimits? Build = null,
    int MaximumCacheBytes = 8 * 1024 * 1024, long MaximumArtifactBytes = 1L << 40);
public sealed record ArtifactFreshnessResult(string State, string Path, DateTime? BuiltUtc, MrpackComparison Comparison, string Error)
{
    public bool FromCache { get; init; }
}
internal sealed record ArtifactFreshnessCache(int SchemaVersion, string Fingerprint, ArtifactFreshnessResult Status);
[JsonSourceGenerationOptions(MaxDepth = 32)]
[JsonSerializable(typeof(ArtifactFreshnessCache))]
internal partial class ArtifactFreshnessJsonContext : JsonSerializerContext;

public static class ArtifactFreshness
{
    private static readonly MrpackComparison Empty = new([], [], []);
    public static async Task<ArtifactFreshnessResult> ReadAsync(ProjectContext project, string configurationDirectory, string stateDirectory,
        Func<PackwizObservation> resolvePackwiz, string nativeHost, ArtifactFreshnessLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(project); limits ??= new();
        if (limits.MaximumCacheBytes is <= 0 or > 64 * 1024 * 1024 || limits.MaximumArtifactBytes <= 0)
            throw new DomainException("Build.InvalidFreshnessPolicy", "Artifact freshness quotas must be positive and bounded", "use bounded artifact freshness quotas");
        FileInfo? baseline = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var identity = WindowsProjectIdentity.Open(project.Root);
            try { baseline = ProjectDiff.Latest(project, project, cancellation); }
            catch (DomainException error) when (error.Id == "ModpackTools.Diff.BaselineNotFound") { return new("missing", "", null, Empty, ""); }
            var config = MutationConfiguration.Resolve(project, configurationDirectory);
            var directory = TransactionFiles.Within(config, "native-build-health/v2");
            var cachePath = TransactionFiles.Within(directory, TextHash.Compute(identity.Value.CanonicalRoot.ToLowerInvariant()) + ".json");
            var before = Fingerprint();
            var cached = ReadCache();
            if (cached is not null)
            {
                AssertUnchanged(); return cached with { FromCache = true };
            }
            var baselineDocument = MrpackSnapshot.ReadDocument(baseline.FullName, limits.Build?.Archive, cancellation);
            var observed = await ProjectDiff.CompareAsync(project, config, stateDirectory, resolvePackwiz, nativeHost, limits.Build, cancellation).ConfigureAwait(false);
            if (!observed.Diagnostics.IsEmpty) return Unknown(string.Join("; ", observed.Diagnostics.Select(item => item.Message)));
            if (!StringComparer.OrdinalIgnoreCase.Equals(observed.Result.BaselinePath, baseline.FullName) || observed.Result.BaselineUtc != baseline.LastWriteTimeUtc)
                return Unknown("The selected build changed during artifact verification");
            var currentDocument = observed.Result.CurrentArtifact ?? throw new InvalidOperationException("Validated comparison did not retain its owned document.");
            var comparison = MrpackSnapshot.Compare(Records(baselineDocument), Records(currentDocument), cancellation: cancellation);
            var result = new ArtifactFreshnessResult(comparison.Total == 0 ? "current" : "stale", baseline.FullName, baseline.LastWriteTimeUtc, comparison, "");
            AssertUnchanged();
            AtomicJsonCacheFile.Write(directory, cachePath, limits.MaximumCacheBytes,
                writer => JsonSerializer.Serialize(writer, new ArtifactFreshnessCache(2, before, result), ArtifactFreshnessJsonContext.Default.ArtifactFreshnessCache),
                () => new DomainException("Build.FreshnessCacheLimit", "Artifact freshness cache exceeds its byte quota", "increase the bounded cache quota", "LimitsExceeded", cachePath),
                details => new DomainException("Build.FreshnessCacheWriteFailed", "Artifact freshness cache could not be written", "check configuration-directory permissions", "WriteError", cachePath, details: details),
                beforeCommit: () => { WindowsProjectIdentity.GuardAncestors(cachePath); AssertUnchanged(); });
            return result;

            string Fingerprint()
            {
                cancellation.ThrowIfCancellationRequested(); identity.AssertCurrent();
                var latest = ProjectDiff.Latest(project, project, cancellation);
                var source = SourceFingerprint.All(SourceTree.ReadForHealth(project.Root, limits.Sources, cancellation));
                var hash = TransactionFiles.Hash(latest.FullName, limits.MaximumArtifactBytes, cancellation)
                    ?? throw new DomainException("Build.ArtifactChanged", "The selected build disappeared", "retry artifact verification", "InvalidOperation");
                return TextHash.Compute(identity.Value.FileIdentity + "|" + source + "|" + latest.Name + "|" + latest.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture) + "|" + hash + "|artifact-validator=2");
            }
            void AssertUnchanged()
            {
                if (Fingerprint() != before) throw new DomainException("Build.ArtifactChanged", "Project inputs or the selected build changed during artifact verification", "retry artifact verification", "InvalidOperation");
            }
            ArtifactFreshnessResult? ReadCache()
            {
                if (!File.Exists(cachePath)) return null;
                try
                {
                    WindowsProjectIdentity.GuardAncestors(cachePath);
                    using var file = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    WindowsProjectIdentity.AssertStreamBytes(file.SafeFileHandle, limits.MaximumCacheBytes, cachePath);
                    if (file.Length > limits.MaximumCacheBytes) return null;
                    var cache = JsonSerializer.Deserialize(file, ArtifactFreshnessJsonContext.Default.ArtifactFreshnessCache);
                    if (cache is not { SchemaVersion: 2, Status: { } status } || cache.Fingerprint != before || status.State is not ("current" or "stale")
                        || !StringComparer.OrdinalIgnoreCase.Equals(status.Path, baseline.FullName) || status.BuiltUtc != baseline.LastWriteTimeUtc || status.Error != "" || status.Comparison is not { } comparison
                        || comparison.Added.IsDefault || comparison.Removed.IsDefault || comparison.Changed.IsDefault
                        || (status.State == "current") != (comparison.Total == 0)) return null;
                    if (comparison.Added.Any(Invalid) || comparison.Removed.Any(Invalid) || comparison.Changed.Any(item => item is null || item.Key is null || item.Kind is null || item.Path is null || item.Before is null || item.After is null)) return null;
                    return status;
                }
                catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or FormatException or ArgumentException or OverflowException) { return null; }
            }
        }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Artifact verification was cancelled", "retry when ready", "OperationStopped"); }
        catch (DomainException error) when (error.Id != "ModpackTools.Core.OperationCancelled" && error.Data["ModpackTools.RetainStage"] is not true) { return Unknown(error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException) { return Unknown(error.Message); }
        ArtifactFreshnessResult Unknown(string error) => new("unknown", baseline?.FullName ?? "", baseline?.LastWriteTimeUtc, Empty, error);
    }
    private static bool Invalid(MrpackRecord item) => item is null || item.Key is null || item.Kind is null || item.Path is null || item.Fingerprint is null;
    private static ImmutableArray<MrpackRecord> Records(MrpackDocument document) => document.Records.Add(new("pack:summary", "PACK", "summary", document.Summary));
}
