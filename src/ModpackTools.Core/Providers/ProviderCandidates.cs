using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core.Data;
using ModpackTools.Core.Source;

namespace ModpackTools.Core.Providers;

public enum CandidateAvailability { Unknown, KnownEmpty, KnownAvailable }
public enum ProviderObservationSource { Supplied, Cache, Checked }
public sealed record ProviderCandidateEvidence(string Path, DateTime? ModifiedUtc, ProviderCacheStatus Status);
public sealed record ProviderCandidateObservation(ProviderProject? Project,
    ImmutableArray<ProviderVersion> Versions, ProviderCacheStatus ProjectStatus,
    ProviderCacheStatus? ListStatus, string? Endpoint, bool Checked,
    ProviderObservationSource ProjectSource, ProviderCandidateEvidence? ProjectCache, ProviderCandidateEvidence? ListCache)
{
    public CandidateAvailability Availability => Project is null || ListStatus is not (ProviderCacheStatus.Fresh or ProviderCacheStatus.Empty)
        ? CandidateAvailability.Unknown : Versions.IsEmpty ? CandidateAvailability.KnownEmpty : CandidateAvailability.KnownAvailable;
}

public static class ProviderCandidates
{
    public static async Task<ProviderCandidateObservation> ObserveAsync(string configurationDirectory,
        string projectId, string minecraftVersion, string loader, DateTimeOffset now,
        ModrinthVersionClient? checkedClient = null, ProviderProject? suppliedProject = null,
        ProviderCacheReadLimits? offlineLimits = null, int maximumOfflineVersions = 10000)
        => await ObserveCore(configurationDirectory, projectId, minecraftVersion, loader, now, checkedClient, suppliedProject,
            offlineLimits, maximumOfflineVersions, new()).ConfigureAwait(false);

    internal static Task<ProviderCandidateObservation> ObserveWithBudget(string configurationDirectory, string projectId,
        string minecraftVersion, string loader, DateTimeOffset now, ModrinthVersionClient? checkedClient,
        ProviderCacheReadLimits? offlineLimits, ProviderModelBudget models) =>
        ObserveCore(configurationDirectory, projectId, minecraftVersion, loader, now, checkedClient, null, offlineLimits, 10000, models);

    private static async Task<ProviderCandidateObservation> ObserveCore(string configurationDirectory, string projectId,
        string minecraftVersion, string loader, DateTimeOffset now, ModrinthVersionClient? checkedClient,
        ProviderProject? suppliedProject, ProviderCacheReadLimits? offlineLimits, int maximumOfflineVersions, ProviderModelBudget models)
    {
        if (checkedClient is not null && (offlineLimits is not null || maximumOfflineVersions != 10000))
            throw new DomainException("Metadata.InvalidProviderCachePolicy", "Checked candidate lookup uses its client's acquisition policy",
                "configure bounded decoding and returned versions when creating the client", target: "OfflinePolicy");
        var limits = offlineLimits;
        var maximumVersions = maximumOfflineVersions;
        limits ??= new(); ProviderVersionCache.Validate(limits); ProviderProject.ValidateId(projectId);
        if (maximumVersions <= 0) throw new DomainException("Metadata.InvalidProviderCachePolicy", "Candidate version limit is invalid",
            "use a positive quota", target: "MaximumVersions");
        var projectStatus = ProviderCacheStatus.Fresh;
        var project = suppliedProject;
        ProviderCandidateEvidence? projectEvidence = null, listEvidence = null;
        var projectSource = project is not null ? ProviderObservationSource.Supplied
            : checkedClient is not null ? ProviderObservationSource.Checked : ProviderObservationSource.Cache;
        if (project is not null && !StringComparer.Ordinal.Equals(project.Id, projectId)) throw Invalid("Supplied project identity does not match the requested project.");
        if (project is null && checkedClient is not null) project = await checkedClient.GetProjectAsync(projectId).ConfigureAwait(false);
        else if (project is null)
        {
            project = Read("project/" + projectId, (root, dates) =>
            {
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0) return null;
                    if (root.GetArrayLength() != 1) throw new FormatException("Provider project cache must contain one project.");
                    root = root[0];
                }
                var value = ProviderProject.Decode(root, dates, limits.MaximumCollectionEntries, models.Entry);
                if (value.Id != projectId) throw Invalid("Cached project identity does not match the requested project.");
                return value;
            }, out projectStatus, out projectEvidence);
        }
        if (project is null) return new(null, [], projectStatus, null, null, checkedClient is not null, projectSource, projectEvidence, null);
        var endpoint = project.CandidateEndpoint(minecraftVersion, loader);
        ProviderCacheStatus listStatus;
        ImmutableArray<ProviderVersion> versions;
        if (checkedClient is not null)
        {
            versions = await checkedClient.GetCandidateVersionsAsync(project, minecraftVersion, loader).ConfigureAwait(false);
            listStatus = versions.IsEmpty ? ProviderCacheStatus.Empty : ProviderCacheStatus.Fresh;
        }
        else
        {
            var value = Read(endpoint, (root, dates) =>
            {
                if (root.ValueKind != JsonValueKind.Array) throw new FormatException("Provider candidate cache must be an array.");
                if (root.GetArrayLength() > maximumVersions) throw Limit("MaximumVersions");
                var entries = 0;
                var result = ImmutableArray.CreateBuilder<ProviderVersion>(root.GetArrayLength());
                foreach (var row in root.EnumerateArray())
                {
                    models.Version();
                    var version = ProviderVersionCache.Decode(row, dates, limits.MaximumCollectionEntries - entries, models.Entry);
                    if (version.ProjectId != projectId) throw Invalid("Cached candidate belongs to another project.");
                    entries += version.Files.Length + version.Dependencies.Length + version.Loaders.Length + version.GameVersions.Length;
                    result.Add(version);
                }
                return new VersionList(result.ToImmutable());
            }, out listStatus, out listEvidence);
            versions = value?.Values ?? [];
        }
        return new(project, checkedClient is null ? Order(versions) : versions, projectStatus, listStatus, endpoint, checkedClient is not null, projectSource, projectEvidence, listEvidence);

        T? Read<T>(string query, Func<JsonElement, CacheDateTable, T?> decode, out ProviderCacheStatus status,
            out ProviderCandidateEvidence? evidence) where T : class
        {
            var path = Path.Combine(Path.GetFullPath(configurationDirectory), "metadata", TextHash.Compute(query) + ".json");
            status = ProviderCacheStatus.Missing;
            evidence = null;
            DateTime? modified = null;
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                modified = File.GetLastWriteTimeUtc(file.SafeFileHandle);
                if (modified.Value.Ticks <= now.UtcDateTime.Ticks - TimeSpan.TicksPerDay)
                { status = ProviderCacheStatus.Expired; return null; }
                var decoded = ProviderVersionCache.ReadDocument(file, limits, models.Bytes);
                status = ProviderCacheStatus.Empty;
                if (decoded is null) return null;
                using var document = decoded.Document;
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Null) return null;
                JsonDisplayProjection.Validate(root);
                var value = decode(root, decoded.Dates);
                status = value is null || value is VersionList { Values.IsEmpty: true } ? ProviderCacheStatus.Empty : ProviderCacheStatus.Fresh;
                return value;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return null; }
            catch (CacheJsonLimitException exception) { throw Limit(path, exception.Message); }
            catch (Newtonsoft.Json.JsonReaderException exception) when (exception.Message.StartsWith("The reader's MaxDepth of ", StringComparison.Ordinal))
            { throw Limit(path, exception.Message); }
            catch (JsonException exception) when (exception.Message.StartsWith("The maximum configured depth of ", StringComparison.Ordinal))
            { throw Limit(path, exception.Message); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or JsonException or Newtonsoft.Json.JsonException)
            { throw Invalid($"{path}: {exception.Message}"); }
            finally { evidence = new(path, modified, status); }
        }
    }

    internal static ImmutableArray<ProviderVersion> Order(ImmutableArray<ProviderVersion> versions)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        return [.. versions.Select((version, index) => (Version: version, Index: index, Ticks: PublicationDate(version).Ticks))
            .OrderByDescending(value => value.Ticks).ThenBy(value => value.Version.Id, comparer).ThenBy(value => value.Index)
            .Select(value => value.Version)];
    }
    internal static DateTime PublicationDate(ProviderVersion version)
    {
        if (version.Published.Value is { } date) return date;
        if (version.Published.Text.Length != 0 && DateTime.TryParse(version.Published.Text, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed)) return parsed;
        throw new DomainException("Metadata.InvalidCandidateDate", "Candidate publication date is invalid",
            "refresh or inspect provider metadata", "InvalidData", version.Id);
    }
    private sealed record VersionList(ImmutableArray<ProviderVersion> Values);
    private static DomainException Invalid(string details) => new("Metadata.InvalidProviderResponse", "Provider candidate metadata is invalid",
        "refresh or inspect provider metadata", "InvalidData", details: details);
    private static DomainException Limit(string target, string? details = null) => new("Metadata.ProviderCacheLimit", "Provider candidate cache exceeds its resource limit",
        "use explicit bounded quotas", "LimitsExceeded", target, details: details);
}
