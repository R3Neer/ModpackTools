using System.Globalization;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Providers;

// One planning invocation owns these lazy snapshots. Repeated numeric selectors
// cannot switch to a concurrently replaced search/version list halfway through.
public sealed class SavedProviderChoices
{
    private sealed record Snapshot(OwnedCacheData Owned, string Path)
    {
        public string Text(string name) => CacheJson.String(CacheFields.Field(Owned.Data, name), Owned.Dates);
        public bool Fresh(DateTimeOffset now) => CacheJson.Timestamp(CacheFields.Field(Owned.Data, "CreatedUtc"), Owned.Dates, out var value)
            && (now - value.ToUniversalTime()).TotalHours <= 24;
        public CacheRows Rows(string field) => new(CacheFields.Field(Owned.Data, field), Path, Owned.Dates);
    }
    private readonly Lazy<Snapshot?> search;
    private readonly Lazy<Snapshot?> versions;
    private readonly DateTimeOffset now;
    private readonly ReferenceCacheReadLimits limits;

    public SavedProviderChoices(string configurationDirectory, DateTimeOffset now, ReferenceCacheReadLimits? limits = null)
    {
        this.now = now; this.limits = limits ?? new(); this.limits.Validate();
        var directory = Path.GetFullPath(configurationDirectory);
        search = new(() => Read("Search", Path.Combine(directory, "last-search.json")));
        versions = new(() => Read("Versions", Path.Combine(directory, "last-versions.json")));
    }

    public string? ResolveSearchNumber(string selector, ProjectContext project)
    {
        if (!ReferenceCacheResolver.IsNumericSelector(selector)) return null;
        var snapshot = search.Value;
        if (snapshot is null) throw Error("Search.CacheNotFound", $"Search result number '{selector}' cannot be resolved because there is no saved search", "ObjectNotFound", selector);
        return Access("Search", snapshot, () =>
        {
            if (!snapshot.Fresh(now)) throw Error("Search.CacheExpired", "The saved search has expired", "InvalidData");
            var owner = snapshot.Text("ProjectId");
            if (!string.IsNullOrWhiteSpace(owner))
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(owner, project.Id)) throw new DomainException("Search.ProjectMismatch", $"The saved search belongs to project '{owner}', not '{project.Id}'", $"modpack -p {project.Id} content search <query>", "InvalidData", selector);
                if (!StringComparer.OrdinalIgnoreCase.Equals(snapshot.Text("MinecraftVersion"), project.Minecraft ?? "")
                    || !StringComparer.OrdinalIgnoreCase.Equals(snapshot.Text("Loader"), project.Loader ?? ""))
                    throw Error("Search.CompatibilityChanged", "The project compatibility settings changed after the saved search", "InvalidData");
            }
            var rows = Rows(snapshot, "Results", "Search"); int? number = null;
            CacheRowView? selected = null; var count = 0;
            foreach (var row in rows.Enumerate()) { var index = Index(row); number ??= Number(selector); if (index == number) { selected = row; count++; } }
            if (count != 1) throw new DomainException("Search.ResultOutOfRange", $"Search result number '{selector}' does not exist; available range: 1-{rows.Count}", "choose a number shown by the latest modpack content search", target: selector);
            return Text(selected!.Value, "ProjectId");
        });
    }

    public string ResolveVersionChoice(string selector, ProjectContext project, InventoryItem item)
    {
        var snapshot = versions.Value;
        if (snapshot is null) throw new DomainException("Versions.CacheNotFound", $"Version '{selector}' cannot be resolved because no version list has been saved", "modpack content versions " + item.Id, "ObjectNotFound", selector);
        return Access("Versions", snapshot, () =>
        {
            if (!snapshot.Fresh(now)) throw new DomainException("Versions.CacheExpired", "The saved version list has expired", "modpack content versions " + item.Id, "InvalidData");
            if (!StringComparer.OrdinalIgnoreCase.Equals(snapshot.Text("ProjectId"), project.Id) || !StringComparer.OrdinalIgnoreCase.Equals(snapshot.Text("ItemId"), item.Id))
                throw new DomainException("Versions.CacheContextMismatch", $"The saved version list belongs to '{snapshot.Text("ItemName")}' in project '{snapshot.Text("ProjectId")}'", $"modpack -p {project.Id} content versions {item.Id}", "InvalidData", selector);
            var rows = Rows(snapshot, "Versions", "Versions"); var numeric = ReferenceCacheResolver.IsNumericSelector(selector);
            int? number = null; var matches = new List<string>();
            foreach (var row in rows.Enumerate())
                if (numeric ? MatchNumber(row) : StringComparer.OrdinalIgnoreCase.Equals(Text(row, "Id"), selector) || StringComparer.OrdinalIgnoreCase.Equals(Text(row, "VersionNumber"), selector))
                    matches.Add(Text(row, "Id"));
            if (matches.Count == 0) throw new DomainException("Versions.NotFound", $"Compatible version '{selector}' was not found for '{item.Name}'", "modpack content versions " + item.Id, "ObjectNotFound", selector);
            if (matches.Count > 1) throw new DomainException("Versions.Ambiguous", $"Version number '{selector}' identifies more than one Modrinth release",
                "use the exact version ID or a numbered result", target: selector, details: string.Join(", ", matches));
            return matches[0];
            bool MatchNumber(CacheRowView row) { var index = Index(row); number ??= Number(selector); return index == number; }
        });
    }

    private Snapshot? Read(string family, string path)
    {
        var value = LegacyCacheFile.Read(path, limits.DecodeLimits, (file, detail) => Invalid(family, file, detail),
            (file, detail) => new(family + ".CacheReadLimit", "Saved provider choices exceed their reader quota", "refresh a bounded result list", "LimitsExceeded", file, details: detail));
        return value is null || !JsonDisplayProjection.Truthy(new(value.Data)) ? null : new(value, path);
    }
    private CacheRows Rows(Snapshot snapshot, string field, string family)
    {
        var rows = snapshot.Rows(field);
        if (rows.Count > limits.MaximumRows) throw new DomainException(family + ".CacheReadLimit", "Saved provider choices exceed their row quota", "refresh a bounded result list", "LimitsExceeded", snapshot.Path);
        return rows;
    }
    private static string Text(CacheRowView row, string field) => CacheJson.String(CacheFields.Field(row.Data, field), row.Dates);
    private static int Index(CacheRowView row) => CacheJson.Integer(CacheFields.Field(row.Data, "Index"), row.Dates);
    private static int Number(string selector)
    {
        if (!int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            throw new DomainException("Content.InvalidReferenceNumber", "The numbered result exceeds the supported integer range", "choose a displayed result number", target: selector);
        return number;
    }
    private static T Access<T>(string family, Snapshot snapshot, Func<T> read) => CacheFields.Access(snapshot.Path, read, (path, detail) => Invalid(family, path, detail));
    private static DomainException Invalid(string family, string path, string? detail) => new(family + ".InvalidCache", $"{family} cache '{path}' is invalid", "refresh the saved provider result list", "InvalidData", path, details: detail);
    private static DomainException Error(string id, string message, string category, string? target = null) => new(id, message,
        id.StartsWith("Search.", StringComparison.Ordinal) ? "modpack content search <query>" : "modpack content versions <content>", category, target);
}
