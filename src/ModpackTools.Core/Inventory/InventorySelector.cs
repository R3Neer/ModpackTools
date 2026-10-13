using System.Collections.Immutable;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Inventory;

public enum InventoryType { All, Mod, ResourcePack, ShaderPack }
public enum InventoryState { All, Active, Inactive }
public enum InventorySource { Packwiz, Local, Builtin, Missing }
public sealed record RawInventoryQuery(string? Type = "all", string? Category = null, string? Side = null,
    string? Source = null, string? State = "all", string? Match = null);
public sealed record InventorySelection(ProjectContext Project, EditorialMetadata Metadata,
    ImmutableArray<InventoryItem> Mods, ImmutableArray<ActiveResource> ActiveResources,
    ImmutableArray<InventoryItem> InactiveResources, ImmutableArray<InventoryItem> Shaders,
    ImmutableArray<ContentKind> IncludedTypes, ImmutableArray<string> Filters)
{
    public int TotalMatches => checked(Mods.Length + ActiveResources.Length + InactiveResources.Length + Shaders.Length);
}

public static class InventorySelector
{
    private static readonly StringComparer Culture = StringComparer.InvariantCultureIgnoreCase;

    public static InventorySelection Select(RawInventorySnapshot snapshot, RawInventoryQuery query)
    {
        var normalized = Normalize(snapshot.Metadata, query);
        bool Common(string name, string id, string? filename, InventorySource source)
        {
            if (normalized.Source is { } expected && source != expected) return false;
            var search = query.Match;
            if (string.IsNullOrEmpty(search)) return true;
            if (name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                filename?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // A boundary-spanning match necessarily contains the joining space.
            return search.Contains(' ') && $"{name} {id} {filename}".IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        bool Physical(InventoryItem item) => Common(item.Name, item.Id, item.Filename,
            item.Source == ContentSource.Packwiz ? InventorySource.Packwiz : InventorySource.Local);
        var mods = normalized.Type is InventoryType.All or InventoryType.Mod
            ? snapshot.Mods.Where(item => Physical(item) &&
                (string.IsNullOrEmpty(query.Category) || Culture.Equals(item.Category, query.Category)) &&
                (normalized.Side is null || Culture.Equals(item.Side, normalized.Side))).ToImmutableArray() : [];
        var active = normalized.Type is InventoryType.All or InventoryType.ResourcePack && normalized.State != InventoryState.Inactive
            ? snapshot.ResourceView.Active.Where(item => Common(item.Name, item.Id, item.Filename, item.Source switch
            {
                ActiveResourceSource.Packwiz => InventorySource.Packwiz,
                ActiveResourceSource.Local => InventorySource.Local,
                ActiveResourceSource.Builtin => InventorySource.Builtin,
                ActiveResourceSource.Missing => InventorySource.Missing,
                _ => throw new ArgumentException("Unknown resource source.", nameof(snapshot))
            })).ToImmutableArray() : [];
        var inactive = normalized.Type is InventoryType.All or InventoryType.ResourcePack && normalized.State != InventoryState.Active
            ? snapshot.ResourceView.Inactive.Where(Physical).ToImmutableArray() : [];
        var shaders = normalized.Type is InventoryType.All or InventoryType.ShaderPack
            ? snapshot.Shaders.Where(Physical).ToImmutableArray() : [];
        ImmutableArray<ContentKind> included = normalized.Type switch
        {
            InventoryType.Mod => [ContentKind.Mod],
            InventoryType.ResourcePack => [ContentKind.ResourcePack],
            InventoryType.ShaderPack => [ContentKind.ShaderPack],
            _ => [ContentKind.Mod, ContentKind.ResourcePack, ContentKind.ShaderPack]
        };
        return new(snapshot.Project, snapshot.Metadata, mods, active, inactive, shaders, included, normalized.Labels);
    }

    private sealed record Normalized(InventoryType Type, string? Side, InventorySource? Source, InventoryState State, ImmutableArray<string> Labels);

    private static Normalized Normalize(EditorialMetadata metadata, RawInventoryQuery query)
    {
        var type = Type(query.Type ?? "");
        var side = (query.Side ?? "").ToLowerInvariant();
        if (Culture.Equals(side, "host")) side = "server";
        if (side.Length != 0 && !new[] { "client", "server", "both", "unknown" }.Contains(side, Culture))
            throw new DomainException("Inventory.InvalidSide", $"Side '{query.Side}' is not recognized; allowed values: client, host, server, both, unknown",
                "--side <client|host|both|unknown>", target: query.Side);
        var sourceText = (query.Source ?? "").ToLowerInvariant();
        InventorySource? source = sourceText.Length == 0 ? null : Source(sourceText, query.Source!);
        var stateText = (query.State ?? "").ToLowerInvariant();
        var state = State(stateText, query.State ?? "");
        var hasModFilter = !string.IsNullOrEmpty(query.Category) || side.Length != 0;
        if (hasModFilter)
        {
            if (type is not InventoryType.All and not InventoryType.Mod)
                throw new DomainException("Inventory.ModFilterTypeConflict", "Options '--category' and '--side' apply only to mods",
                    "use --type mod or remove the mod-only filters");
            if (state != InventoryState.All)
                throw new DomainException("Inventory.FilterConflict", "Mod filters cannot be combined with '--state'", "remove --state or the mod-only filters");
            type = InventoryType.Mod;
        }
        if (state != InventoryState.All)
        {
            if (type is not InventoryType.All and not InventoryType.ResourcePack)
                throw new DomainException("Inventory.StateFilterTypeConflict", "Option '--state' applies only to resource packs",
                    "use --type resourcepack or remove --state");
            type = InventoryType.ResourcePack;
        }
        if (!string.IsNullOrEmpty(query.Category) && !Culture.Equals(query.Category, "unclassified") && !metadata.Categories.Entries.ContainsKey(query.Category))
        {
            var available = string.Join(", ", metadata.Categories.Entries.Keys.Order(StringComparer.CurrentCultureIgnoreCase).ThenBy(key => key, StringComparer.Ordinal));
            throw new DomainException("Inventory.UnknownCategory", $"Category '{query.Category}' is not defined; available categories: {available}, unclassified",
                "modpack content list --type mod", target: query.Category);
        }
        var labels = ImmutableArray.CreateBuilder<string>();
        if (type != InventoryType.All) labels.Add("type=" + KindName(type));
        if (!string.IsNullOrEmpty(query.Category)) labels.Add("category=" + query.Category);
        if (side.Length != 0) labels.Add("side=" + (Culture.Equals(side, "server") ? "host" : side));
        if (sourceText.Length != 0) labels.Add("source=" + sourceText);
        if (state != InventoryState.All) labels.Add("state=" + stateText);
        if (!string.IsNullOrEmpty(query.Match)) labels.Add("search=" + query.Match);
        return new(type, side.Length == 0 ? null : side, source, state, labels.ToImmutable());
    }

    private static InventoryType Type(string value)
    {
        if (Culture.Equals(value, "all")) return InventoryType.All;
        if (new[] { "mod", "mods" }.Contains(value, Culture)) return InventoryType.Mod;
        if (new[] { "resource", "resources", "resourcepack", "resourcepacks" }.Contains(value, Culture)) return InventoryType.ResourcePack;
        if (new[] { "shader", "shaders", "shaderpack", "shaderpacks" }.Contains(value, Culture)) return InventoryType.ShaderPack;
        throw new DomainException("Inventory.InvalidType", $"Inventory type '{value}' is not recognized; allowed values: all, mod, resourcepack, shaderpack",
            "--type <all|mod|resourcepack|shaderpack>", target: value);
    }

    private static InventorySource Source(string value, string original)
    {
        if (Culture.Equals(value, "packwiz")) return InventorySource.Packwiz;
        if (Culture.Equals(value, "local")) return InventorySource.Local;
        if (Culture.Equals(value, "builtin")) return InventorySource.Builtin;
        if (Culture.Equals(value, "missing")) return InventorySource.Missing;
        throw new DomainException("Inventory.InvalidSource", $"Source '{original}' is not recognized; allowed values: packwiz, local, builtin, missing",
            "--source <packwiz|local|builtin|missing>", target: original);
    }

    private static InventoryState State(string value, string original)
    {
        if (Culture.Equals(value, "all")) return InventoryState.All;
        if (Culture.Equals(value, "active")) return InventoryState.Active;
        if (Culture.Equals(value, "inactive")) return InventoryState.Inactive;
        throw new DomainException("Inventory.InvalidState", $"State '{original}' is not recognized; allowed values: all, active, inactive",
            "--state <all|active|inactive>", target: original);
    }

    private static string KindName(InventoryType type) => type switch
    {
        InventoryType.Mod => "mod",
        InventoryType.ResourcePack => "resourcepack",
        InventoryType.ShaderPack => "shaderpack",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
