using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;

namespace ModpackTools.Core.Providers;

public sealed record CompatibleVersion(ProviderVersion Version, string VersionType, string? Filename, long PublicationTicks)
{
    // Owned persistence evidence only; dependency evaluation uses Version.Dependencies.
    internal string DependencyCacheJson { get; init; } = "[]";

    internal static CompatibleVersion Capture(ProviderVersion version, JsonElement row, CacheDateTable dates,
        Func<JsonElement, string> captureWire)
    {
        JsonElement Field(string name)
        {
            foreach (var (key, value) in JsonDisplayProjection.Properties(row))
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) return value;
            throw new FormatException($"Version listing field '{name}' is missing.");
        }
        foreach (var name in new[] { "name", "version_number", "project_id", "loaders", "game_versions" }) _ = Field(name);
        var type = Field("version_type");
        if (type.ValueKind is JsonValueKind.Array or JsonValueKind.Object) throw new FormatException("Version type must be scalar.");
        var publication = Field("date_published");
        var ticks = DateTime.MinValue.Ticks;
        if (JsonDisplayProjection.Truthy(new(publication)))
        {
            if (version.Published.Value is { } date) ticks = date.Ticks;
            else if (DateTime.TryParse(version.Published.Text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)) ticks = parsed.Ticks;
            else throw new FormatException("Version listing publication date is invalid.");
        }
        var files = Field("files"); var index = 0; var chosen = -1;
        if (files.ValueKind == JsonValueKind.Array)
            foreach (var file in files.EnumerateArray())
            {
                if (chosen < 0 && JsonDisplayProjection.Truthy(JsonDisplayProjection.Field(file, "primary"))) chosen = index;
                index++;
            }
        else if (files.ValueKind != JsonValueKind.Null) throw new FormatException("Version listing files must be an array or null.");
        if (chosen < 0 && !version.Files.IsEmpty) chosen = 0;
        return new(version, CacheJson.String(new(type), dates), chosen < 0 ? null : version.Files[chosen].Filename, ticks)
        { DependencyCacheJson = captureWire(Field("dependencies")) };
    }

    internal static string Endpoint(string projectId, ContentKind kind, string minecraft, string loader)
    {
        _ = CompatibleVersions.Kind(kind);
        ProviderProject.ValidateId(projectId);
        if (minecraft.Length > 256 || loader.Length > 256) throw new DomainException("Metadata.ProviderRequestLimit",
            "Version query values exceed their limit", "use bounded version query values", "LimitsExceeded", "QueryValues");
        static string Singleton(string value)
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) { writer.WriteStartArray(); writer.WriteStringValue(value); writer.WriteEndArray(); }
            return Uri.EscapeDataString(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length));
        }
        var endpoint = "project/" + projectId + "/version?game_versions=" + Singleton(minecraft);
        if (kind == ContentKind.ResourcePack) endpoint += "&loaders=" + Singleton("minecraft");
        else if (kind == ContentKind.Mod && loader.Length != 0) endpoint += "&loaders=" + Singleton(loader.ToLowerInvariant());
        return endpoint + "&include_changelog=false";
    }
}

public sealed record CompatibleVersionRow(int Number, CompatibleVersion Candidate, bool Installed);
public sealed record CompatibleVersionView(string ProjectId, string? MinecraftVersion, string? Loader,
    string ItemId, string ItemName, ContentKind ItemKind, string? InstalledVersionId,
    ImmutableArray<CompatibleVersionRow> Versions);
