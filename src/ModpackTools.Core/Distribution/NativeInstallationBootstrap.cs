using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

internal static class NativeInstallationBootstrap
{
    private static readonly string[] Names = ["modpack.exe", "modpacktools-setup.exe"];
    public static void Ensure(string applicationRoot, NativePreparedPackage version, bool active, CancellationToken cancellation)
    {
        var bin = Path.Combine(applicationRoot, "bin"); var metadata = Path.Combine(bin, "bootstrap.json");
        using var identity = WindowsProjectIdentity.Open(bin); identity.AssertDefaultStreamOnly();
        var allowed = Names.Append("bootstrap.json").ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(bin))
        {
            WindowsProjectIdentity.GuardAncestors(path);
            if (!File.Exists(path) || !allowed.Contains(Path.GetFileName(path))) throw Error("BootstrapChanged", "Stable bootstrap contains unknown contents", path);
        }
        ImmutableArray<NativePackageFile> files;
        if (File.Exists(metadata)) files = Read(metadata);
        else
        {
            if (active) throw Error("BootstrapChanged", "An active installation is missing its stable bootstrap manifest", metadata);
            files = version.Manifest.Files.Where(file => Names.Contains(file.Path, StringComparer.Ordinal)).OrderBy(file => file.Path, StringComparer.Ordinal).ToImmutableArray();
        }
        foreach (var file in files)
        {
            var path = Path.Combine(bin, file.Path);
            if (!File.Exists(path))
            {
                if (active || File.Exists(metadata)) throw Error("BootstrapChanged", "Stable bootstrap file is missing", path);
                using var source = new FileStream(Path.Combine(version.Directory, file.Path), FileMode.Open, FileAccess.Read, FileShare.Read);
                using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                if (NativePackageArchive.Copy(source, target, file.Length, cancellation) != file.Sha256) throw Error("BootstrapChanged", "Verified bootstrap source changed", path);
                target.Flush(flushToDisk: true);
            }
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, file.Length, path, defaultStreamOnly: true);
            if (input.Length != file.Length || NativePackageArchive.Copy(input, Stream.Null, file.Length, cancellation) != file.Sha256)
                throw Error("BootstrapChanged", "Stable bootstrap bytes changed; retain them for inspection", path);
            NativePackageArchive.VerifyNativeExecutable(path);
        }
        if (!File.Exists(metadata))
        {
            using var output = new FileStream(metadata, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteNumber("schema_version", 1); writer.WriteStartArray("files");
                foreach (var file in files)
                { writer.WriteStartObject(); writer.WriteString("path", file.Path); writer.WriteNumber("length", file.Length); writer.WriteString("sha256", file.Sha256); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
            }
            output.Flush(flushToDisk: true);
        }
        identity.AssertCurrent();
    }

    private static ImmutableArray<NativePackageFile> Read(string path)
    {
        try { return ReadCore(path); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException)
        { throw NativeInstallationRoot.Error("BootstrapChanged", "Stable bootstrap manifest JSON is invalid", path, error.Message); }
    }

    private static ImmutableArray<NativePackageFile> ReadCore(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, 32768, path, defaultStreamOnly: true);
        if (input.Length > 32768) throw Error("BootstrapChanged", "Stable bootstrap manifest exceeds its quota", path);
        using var document = JsonDocument.Parse(input, new() { MaxDepth = 8 }); var root = document.RootElement;
        Fields(root, "schema_version", "files");
        if (root.GetProperty("schema_version").GetInt32() != 1 || root.GetProperty("files").GetArrayLength() != 2) throw Error("BootstrapChanged", "Stable bootstrap manifest is invalid", path);
        var files = ImmutableArray.CreateBuilder<NativePackageFile>();
        foreach (var value in root.GetProperty("files").EnumerateArray())
        {
            Fields(value, "path", "length", "sha256");
            var file = new NativePackageFile(value.GetProperty("path").GetString()!, value.GetProperty("length").GetInt64(), value.GetProperty("sha256").GetString()!);
            if (file.Path != Names[files.Count] || file.Length is <= 0 or > 128L * 1024 * 1024 || !NativePackageManifest.HashValue(file.Sha256))
                throw Error("BootstrapChanged", "Stable bootstrap file metadata is invalid", path);
            files.Add(file with { Sha256 = file.Sha256.ToUpperInvariant() });
        }
        return files.ToImmutable();
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("BootstrapChanged", "Stable bootstrap field must be an object", "bootstrap.json");
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!names.Contains(field.Name, StringComparer.Ordinal) || !actual.Add(field.Name)) throw Error("BootstrapChanged", "Stable bootstrap has unknown or duplicate fields", field.Name);
        if (actual.Count != names.Length) throw Error("BootstrapChanged", "Stable bootstrap is missing a required field", "bootstrap.json");
    }
    private static DomainException Error(string id, string message, string path) => NativeInstallationRoot.Error(id, message, path);
}
