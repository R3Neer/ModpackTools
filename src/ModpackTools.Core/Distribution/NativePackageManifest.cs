using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModpackTools.Core.Distribution;

public sealed record NativePackageFile(string Path, long Length, string Sha256);

public sealed record NativePackageManifest(int SchemaVersion, string Product, string Rid, string Version,
    int CoreProtocol, int BootstrapProtocol, string BuildId, ImmutableArray<NativePackageFile> Files)
{
    public static ImmutableArray<string> PayloadPaths { get; } = new[]
    {
        "modpacktools.exe", "modpack.exe", "modpacktools-setup.exe", "cli.nu",
        "Nushell/modpack.nu", "Nushell/Native-Presentation.nu", "Nushell/Native-Views.nu", "Nushell/Render-Native.nu",
        "presentation/r3cli/mod.nu", "presentation/r3cli/resources.json", "presentation/r3cli/LICENSE",
        "presentation/r3cli/hashes.json", "presentation/r3cli/manifest.json", "LICENSE",
        "licenses/Tomlyn.txt", "licenses/Newtonsoft.Json.txt", "README.md"
    }.Order(StringComparer.Ordinal).ToImmutableArray();

    public static NativePackageManifest Create(string version, IEnumerable<NativePackageFile> files)
    {
        var ordered = files.OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => file with { Sha256 = file.Sha256.ToUpperInvariant() }).ToImmutableArray();
        var manifest = new NativePackageManifest(1, "ModpackTools.Native", "win-x64", version, 1, 1, new string('0', 64), ordered);
        manifest.Validate(checkIdentity: false);
        return manifest with { BuildId = manifest.Identity() };
    }

    public byte[] ToBytes()
    {
        Validate();
        return JsonSerializer.SerializeToUtf8Bytes(this, NativePackageJson.Default.NativePackageManifest);
    }

    public void Validate(bool checkIdentity = true)
    {
        if (SchemaVersion != 1 || Product != "ModpackTools.Native" || Rid != "win-x64" || CoreProtocol != 1 || BootstrapProtocol != 1
            || string.IsNullOrWhiteSpace(Version) || Version.Length > 64 || Version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '+')
            || !HashValue(BuildId) || Files.IsDefault || Files.Length != PayloadPaths.Length)
            throw Error("InvalidManifest", "Native package identity or protocol is invalid");
        for (var index = 0; index < Files.Length; index++)
        {
            var file = Files[index];
            if (file is null || file.Path != PayloadPaths[index] || file.Length <= 0 || !HashValue(file.Sha256))
                throw Error("InvalidManifest", "Native package files must match the exact ordered payload", file?.Path);
        }
        if (checkIdentity && !StringComparer.OrdinalIgnoreCase.Equals(BuildId, Identity()))
            throw Error("BuildIdMismatch", "Native package build identity does not match its payload", "package.json");
    }

    internal static NativePackageManifest Read(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 });
            var root = document.RootElement;
            ObjectFields(root, "schema_version", "product", "rid", "version", "core_protocol", "bootstrap_protocol", "build_id", "files");
            var files = root.GetProperty("files");
            if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() != PayloadPaths.Length)
                throw Error("InvalidManifest", "Native package requires the complete fixed payload");
            var result = ImmutableArray.CreateBuilder<NativePackageFile>();
            foreach (var file in files.EnumerateArray())
            {
                ObjectFields(file, "path", "length", "sha256");
                result.Add(new(Text(file, "path"), file.GetProperty("length").GetInt64(), Text(file, "sha256").ToUpperInvariant()));
            }
            var manifest = new NativePackageManifest(root.GetProperty("schema_version").GetInt32(), Text(root, "product"), Text(root, "rid"), Text(root, "version"),
                root.GetProperty("core_protocol").GetInt32(), root.GetProperty("bootstrap_protocol").GetInt32(), Text(root, "build_id").ToUpperInvariant(), result.ToImmutable());
            manifest.Validate(); return manifest;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { throw Error("InvalidManifest", "Native package manifest has invalid JSON fields", "package.json", error.Message); }
    }

    internal static bool HashValue(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    internal static DomainException Error(string id, string message, string? path = null, string? details = null) =>
        new("Package." + id, message, "use a verified native ModpackTools package", "InvalidData", path, details: details);

    private string Identity()
    {
        var identity = new NativePackageIdentity(SchemaVersion, Product, Rid, Version, CoreProtocol, BootstrapProtocol,
            Files.Select(file => file with { Sha256 = file.Sha256.ToUpperInvariant() }).ToImmutableArray());
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity, NativePackageJson.Default.NativePackageIdentity)));
    }

    private static string Text(JsonElement value, string name)
    {
        var field = value.GetProperty(name);
        if (field.ValueKind != JsonValueKind.String) throw Error("InvalidManifest", "Native package field must be a string", name);
        // GetString rejects malformed escaped UTF-16, rather than accepting replacement characters.
        return field.GetString() ?? throw Error("InvalidManifest", "Native package field cannot be null", name);
    }

    private static void ObjectFields(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("InvalidManifest", "Native package field must be an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!expected.Contains(field.Name, StringComparer.Ordinal) || !names.Add(field.Name))
                throw Error("InvalidManifest", "Native package contains an unknown or duplicate field", field.Name);
        if (names.Count != expected.Length) throw Error("InvalidManifest", "Native package is missing a required field");
    }
}

internal sealed record NativePackageIdentity(int SchemaVersion, string Product, string Rid, string Version,
    int CoreProtocol, int BootstrapProtocol, ImmutableArray<NativePackageFile> Files);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(NativePackageManifest))]
[JsonSerializable(typeof(NativePackageIdentity))]
internal sealed partial class NativePackageJson : JsonSerializerContext;
