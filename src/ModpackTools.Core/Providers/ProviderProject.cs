using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Providers;

public enum ProviderContentKind { Mod, ResourcePack, ShaderPack }

public sealed record ProviderProject(string Id, string Title, string RawKind,
    ImmutableArray<string> Environments, string ClientSide, string ServerSide)
{
    public ProviderContentKind Kind => RawKind.ToLowerInvariant() switch
    {
        "mod" => ProviderContentKind.Mod,
        "resourcepack" => ProviderContentKind.ResourcePack,
        "shader" or "shaderpack" => ProviderContentKind.ShaderPack,
        _ => throw new DomainException("Content.UnsupportedKind", $"Unsupported content kind '{RawKind}'",
            "select a mod, resource pack or shader pack", target: RawKind)
    };

    public string CandidateEndpoint(string minecraftVersion, string loader)
    {
        ValidateId(Id);
        ArgumentNullException.ThrowIfNull(minecraftVersion);
        ArgumentNullException.ThrowIfNull(loader);
        if (minecraftVersion.Length > 256 || loader.Length > 256)
            throw new DomainException("Metadata.ProviderRequestLimit", "Candidate query values exceed their limit",
                "use a Minecraft version and loader of at most256 characters", "LimitsExceeded", "MaximumQueryValueCharacters");
        var kind = Kind;
        var query = "project/" + Id + "/version?game_versions=" + Encode([minecraftVersion]);
        string[] loaders = kind switch
        {
            ProviderContentKind.ResourcePack => ["minecraft"],
            ProviderContentKind.Mod when loader.Equals("quilt", StringComparison.OrdinalIgnoreCase) => ["quilt", "fabric"],
            ProviderContentKind.Mod => [loader],
            _ => []
        };
        return loaders.Length == 0 ? query : query + "&loaders=" + Encode(loaders);
    }

    internal static void ValidateId(string id)
    {
        if (id.Length is < 1 or > 256 || id.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new DomainException("Metadata.InvalidProjectId", "Provider project ID must contain ASCII letters and digits",
                "use the canonical provider project ID", target: id);
    }

    internal static ProviderProject Decode(JsonElement root, CacheDateTable dates, int maximumEntries, Action? entryConsumed = null)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Provider project must be an object.");
        var id = Scalar("id"); ValidateId(id);
        var environments = ImmutableArray.CreateBuilder<string>();
        var field = Field("environment");
        if (field.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (field.ValueKind != JsonValueKind.Array) throw new FormatException("Provider project environment must be an array.");
            foreach (var item in field.EnumerateArray())
            {
                if (environments.Count >= maximumEntries) throw new DomainException("Metadata.ProviderCacheLimit",
                    "Provider environments exceed their entry limit", "use explicit bounded quotas", "LimitsExceeded", "MaximumCollectionEntries");
                if (item.ValueKind != JsonValueKind.String) throw new FormatException("Provider environments must be strings.");
                entryConsumed?.Invoke(); environments.Add(CacheJson.String(new(item), dates));
            }
        }
        return new(id, Scalar("title"), Scalar("project_type"), environments.ToImmutable(), Scalar("client_side"), Scalar("server_side"));

        JsonElement Field(string name)
        {
            foreach (var (key, value) in JsonDisplayProjection.Properties(root))
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) return value;
            return default;
        }
        string Scalar(string name)
        {
            var value = Field(name);
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "";
            if (value.ValueKind != JsonValueKind.String) throw new FormatException("Provider project text fields must be strings.");
            return CacheJson.String(new(value), dates);
        }
    }

    private static string Encode(string[] values)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        { writer.WriteStartArray(); foreach (var value in values) writer.WriteStringValue(value); writer.WriteEndArray(); }
        return Uri.EscapeDataString(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
    }
}
