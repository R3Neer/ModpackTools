using System.Globalization;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public sealed record ReferenceLookupPolicy
{
    public ReferenceLookupPolicy(IEnumerable<string>? allowedKinds, bool requirePackwiz = false)
    {
        AllowedKinds = allowedKinds?.ToImmutableArray();
        RequirePackwiz = requirePackwiz;
    }
    public ImmutableArray<string>? AllowedKinds { get; }
    public bool RequirePackwiz { get; }
    public static ReferenceLookupPolicy Default { get; } = new(["mod", "resourcepack", "shaderpack"]);
}

public static partial class ReferenceCacheResolver
{
    [GeneratedRegex("^[1-9][0-9]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex NumberPattern();
    internal static bool IsNumericSelector(string selector) => NumberPattern().IsMatch(selector);

    public static CachedReference? ResolveFromDirectory(string configurationDirectory, string selector, string projectId,
        DateTimeOffset nowUtc, ReferenceLookupPolicy? policy = null, ReferenceCacheReadLimits? limits = null)
    {
        if (!IsNumericSelector(selector)) return null;
        return Resolve(ReferenceCacheReader.Read(configurationDirectory, limits), selector, projectId, nowUtc, policy);
    }

    public static string? ResolveSelectorFromDirectory(string configurationDirectory, string selector, string projectId,
        DateTimeOffset nowUtc, ReferenceLookupPolicy? policy = null, ReferenceCacheReadLimits? limits = null)
    {
        if (!IsNumericSelector(selector)) return null;
        return ResolveSelector(ReferenceCacheReader.Read(configurationDirectory, limits), selector, projectId, nowUtc, policy);
    }

    public static CachedReference? Resolve(CachedInventorySnapshot? snapshot, string selector, string projectId,
        DateTimeOffset nowUtc, ReferenceLookupPolicy? policy = null)
    {
        var row = ResolveRow(snapshot, selector, projectId, nowUtc, policy);
        return row is { } selected ? new(selected.Data, selected.Path, selected.Dates) : null;
    }

    public static string? ResolveSelector(CachedInventorySnapshot? snapshot, string selector, string projectId,
        DateTimeOffset nowUtc, ReferenceLookupPolicy? policy = null)
    {
        var row = ResolveRow(snapshot, selector, projectId, nowUtc, policy);
        if (row is not { } selected) return null;
        return CacheFields.Access(selected.Path, () => CacheJson.String(
            CacheFields.Field(CacheFields.ReturnedData(selected.Data), "Selector"), selected.Dates));
    }

    private static CacheRowView? ResolveRow(CachedInventorySnapshot? snapshot, string selector, string projectId,
        DateTimeOffset nowUtc, ReferenceLookupPolicy? policy)
    {
        if (!IsNumericSelector(selector)) return null;
        policy ??= ReferenceLookupPolicy.Default;
        if (snapshot is null || !snapshot.HasValue)
            throw new DomainException("Inventory.ReferenceCacheNotFound",
                $"Inventory reference number '{selector}' cannot be resolved because no inventory has been saved",
                "modpack content list", "ObjectNotFound", selector);
        if (snapshot.SchemaVersion != 1 || snapshot.Results is null || string.IsNullOrWhiteSpace(snapshot.ProjectId))
            throw new DomainException("Inventory.InvalidReferenceCache",
                "The saved inventory reference cache has an unsupported or incomplete structure", "modpack content list", "InvalidData");
        if (!snapshot.IsFresh(nowUtc))
            throw new DomainException("Inventory.ReferenceCacheExpired", "The saved inventory references have expired", "modpack content list", "InvalidData");
        if (!string.Equals(snapshot.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Inventory.ReferenceProjectMismatch", $"The saved inventory belongs to project '{snapshot.ProjectId}', not '{projectId}'",
                $"modpack -p {projectId} content list", "InvalidData", selector);
        var matches = 0;
        CacheRowView selected = default;
        int? number = null;
        var rows = snapshot.Results!;
        foreach (var row in rows.Enumerate())
        {
            var index = row.Index;
            // Legacy casts the selector only after the first row's Index succeeds.
            number ??= CacheFields.Access(snapshot.Path, () => int.Parse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture));
            if (index == number) { matches++; selected = row; }
        }
        if (matches != 1)
        {
            if (rows.Count == 0)
                throw new DomainException("Inventory.ReferenceOutOfRange", "The saved inventory view contains no numbered items",
                    "run modpack content list without filters, or use broader filters", "InvalidArgument", selector);
            throw new DomainException("Inventory.ReferenceOutOfRange", $"Inventory reference number '{selector}' does not exist; available range: 1-{rows.Count}",
                "choose a number shown by the latest modpack content list", "InvalidArgument", selector);
        }
        var kind = selected.Kind;
        if (policy.AllowedKinds is not { } allowed || !allowed.Contains(kind, StringComparer.InvariantCultureIgnoreCase))
            throw new DomainException("Inventory.ReferenceKindMismatch", $"Inventory reference '{selector}' points to '{kind}', which is not accepted by this command",
                "choose a compatible number from modpack content list", "InvalidArgument", selector);
        if (policy.RequirePackwiz)
        {
            var source = selected.Source;
            if (!string.Equals(source, "packwiz", StringComparison.InvariantCultureIgnoreCase))
                throw new DomainException("Inventory.ReferenceNotUpdatable", $"Inventory reference '{selector}' points to '{source}' content, which Packwiz cannot update",
                    "choose Packwiz-managed content from modpack content list --source packwiz", "InvalidOperation", selector);
        }
        return selected;
    }
}
