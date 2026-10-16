using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core.Source;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

public sealed record NativeVersionReference(string BuildId, string Version, string FileIdentity, string ArchiveSha256);
public sealed record NativeControlGeneration(int SchemaVersion, string Generation, NativeVersionReference Current,
    string? PreviousBuildId, ImmutableArray<NativeVersionReference> Retired);
public sealed record NativeControlSwitch(NativeControlGeneration Current, NativeControlGeneration? Previous,
    bool Applied, ImmutableArray<TransactionDiagnostic> Diagnostics);

public static class NativeInstallationControl
{
    private const int MaximumControlBytes = 64 * 1024;

    public static NativeControlGeneration? Read(string controlRoot)
    {
        WindowsProjectIdentity.GuardAncestors(controlRoot);
        if (!Directory.Exists(controlRoot)) throw Error("MissingControl", "Installation control directory does not exist", controlRoot);
        VerifyTopology(controlRoot);
        var path = Path.Combine(controlRoot, "current.json");
        if (!File.Exists(path))
        {
            if (File.Exists(Path.Combine(controlRoot, "Nushell", "modpack.nu"))) throw Error("InvalidControl", "An initial control tree cannot contain an unpaired bootstrap", controlRoot);
            return null;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        WindowsProjectIdentity.AssertStreamBytes(stream.SafeFileHandle, MaximumControlBytes, path, defaultStreamOnly: true);
        if (stream.Length > MaximumControlBytes) throw Error("ControlLimit", "Installation control exceeds its byte quota", path);
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 }); var root = document.RootElement;
            Fields(root, "schema_version", "generation", "current", "previous_build_id", "retired");
            ReferenceFields(root.GetProperty("current"));
            if (root.GetProperty("retired").ValueKind != JsonValueKind.Array || root.GetProperty("retired").GetArrayLength() > 128)
                throw Error("InvalidControl", "Installation retired-version list is invalid", path);
            foreach (var value in root.GetProperty("retired").EnumerateArray()) ReferenceFields(value);
            var result = JsonSerializer.Deserialize(bytes, NativeControlJson.Default.NativeControlGeneration)
                ?? throw Error("InvalidControl", "Installation control cannot be null", path);
            Validate(result, path); return result;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { throw Error("InvalidControl", "Installation control JSON is invalid", path, error.Message); }
    }

    public static async Task<NativeControlSwitch> SwitchAsync(ProjectPhysicalIdentity controlRoot, string stateDirectory,
        NativePreparedPackage version, string? expectedGeneration, CancellationToken cancellation = default,
        Func<TransactionEvent, ValueTask>? observer = null, Func<string, CancellationToken, Task>? verifyBootstrap = null)
    {
        ArgumentNullException.ThrowIfNull(controlRoot); ArgumentNullException.ThrowIfNull(version);
        NativePackageArchive.Verify(version, cancellation: cancellation);
        using var protectedVersion = WindowsProjectIdentity.Open(version.Directory);
        if (protectedVersion.Value != version.Identity) throw Error("VersionChanged", "Selected version directory identity changed", version.Directory);
        using var protectedControl = WindowsProjectIdentity.Open(controlRoot.CanonicalRoot);
        if (protectedControl.Value != controlRoot) throw Error("ControlChanged", "Installation control directory identity changed", controlRoot.CanonicalRoot);
        var applicationRoot = Path.GetDirectoryName(controlRoot.CanonicalRoot)!;
        NativeRetirement.AssertAvailable(applicationRoot, version.Manifest.BuildId);
        var expectedVersionPath = Path.Combine(applicationRoot, "versions", version.Manifest.BuildId);
        if (Path.GetFileName(controlRoot.CanonicalRoot) != "control" || !StringComparer.OrdinalIgnoreCase.Equals(expectedVersionPath, version.Identity.CanonicalRoot))
            throw Error("UnsafeVersion", "Selected version must belong to this installation's immutable version store", version.Directory);
        var selected = new NativeVersionReference(version.Manifest.BuildId, version.Manifest.Version, version.Identity.FileIdentity, version.ArchiveSha256);
        NativeControlGeneration? previous = null;
        var tree = new SourceTreeReadLimits(MaximumFiles: 2, MaximumDirectories: 2, MaximumBytes: 128 * 1024, MaximumPathCharacters: 32768);
        var result = await FileTreeTransaction.ExecuteAsync(controlRoot.CanonicalRoot, stateDirectory, Prepare, VerifyStage,
            limits: new(Tree: tree, RecoveryTree: tree, MaximumChanges: 2), cancellation: cancellation, observer: observer).ConfigureAwait(false);
        return new(result.Result, previous, result.Applied, result.Diagnostics);

        Task<NativeControlGeneration> Prepare(string stage, CancellationToken token)
        {
            protectedControl.AssertCurrent(); protectedVersion.AssertCurrent(); NativePackageArchive.Verify(version, cancellation: token);
            previous = Read(stage);
            if (!StringComparer.Ordinal.Equals(previous?.Generation, expectedGeneration))
                throw Error("GenerationChanged", "Installation changed after it was selected", controlRoot.CanonicalRoot);
            var same = previous?.Current.BuildId == selected.BuildId;
            if (same && (previous!.Current.FileIdentity != selected.FileIdentity || previous.Current.Version != selected.Version))
                throw Error("VersionChanged", "The active version's immutable identity changed", version.Directory);
            var retired = previous is null ? ImmutableArray<NativeVersionReference>.Empty
                : previous.Retired.Append(previous.Current).Where(value => value.BuildId != selected.BuildId)
                    .DistinctBy(value => value.BuildId, StringComparer.Ordinal).ToImmutableArray();
            var next = same ? previous! : new NativeControlGeneration(1, Guid.NewGuid().ToString("N"), selected, previous?.Current.BuildId, retired);
            Validate(next, stage);
            var currentBytes = JsonSerializer.SerializeToUtf8Bytes(next, NativeControlJson.Default.NativeControlGeneration);
            var bootstrap = Bootstrap(version.Directory);
            if (currentBytes.Length > MaximumControlBytes || bootstrap.Length > MaximumControlBytes) throw Error("ControlLimit", "Installation control exceeds its byte quota", stage);
            Directory.CreateDirectory(Path.Combine(stage, "Nushell"));
            Write(Path.Combine(stage, "current.json"), currentBytes);
            Write(Path.Combine(stage, "Nushell", "modpack.nu"), bootstrap);
            return Task.FromResult(next);
        }
        async Task VerifyStage(string stage, CancellationToken token)
        {
            _ = Read(stage);
            if (verifyBootstrap is not null) await verifyBootstrap(Path.Combine(stage, "Nushell", "modpack.nu"), token).ConfigureAwait(false);
            protectedVersion.AssertCurrent(); NativePackageArchive.Verify(version, cancellation: token);
            _ = Read(controlRoot.CanonicalRoot);
            protectedControl.AssertCurrent();
        }
    }

    internal static byte[] Bootstrap(string versionDirectory)
    {
        var module = NuLiteral(Path.Combine(versionDirectory, "Nushell", "modpack.nu"));
        var executable = NuLiteral(Path.Combine(versionDirectory, "modpacktools.exe"));
        try
        {
            return new UTF8Encoding(false, true).GetBytes("# Managed ModpackTools bootstrap protocol 1.\nexport use " + module
                + " *\nexport-env { $env.MODPACKTOOLS_CORE_PATH = " + executable + " }\n");
        }
        catch (EncoderFallbackException error)
        { throw Error("InvalidBootstrapPath", "Installation paths must be representable as valid UTF-8", versionDirectory, error.Message); }
    }

    internal static async Task<NativeControlSwitch> PruneAsync(string root, string state, string expectedGeneration, string build,
        CancellationToken cancellation)
    {
        NativeControlGeneration? previous = null;
        var tree = new SourceTreeReadLimits(MaximumFiles: 2, MaximumDirectories: 2, MaximumBytes: 128 * 1024, MaximumPathCharacters: 32768);
        var result = await FileTreeTransaction.ExecuteAsync(root, state, (stage, token) =>
        {
            token.ThrowIfCancellationRequested();
            previous = Read(stage) ?? throw Error("NotReady", "Installation has no active version", root);
            if (previous.Generation != expectedGeneration || previous.Current.BuildId == build)
                throw Error("GenerationChanged", "Installation changed before retirement was finalized", root);
            var next = previous with
            {
                Generation = Guid.NewGuid().ToString("N"),
                Retired = previous.Retired.Where(value => value.BuildId != build).ToImmutableArray(),
                PreviousBuildId = previous.PreviousBuildId == build ? null : previous.PreviousBuildId
            };
            Validate(next, root); Write(Path.Combine(stage, "current.json"), JsonSerializer.SerializeToUtf8Bytes(next, NativeControlJson.Default.NativeControlGeneration));
            return Task.FromResult(next);
        }, (stage, token) => { token.ThrowIfCancellationRequested(); _ = Read(stage); return Task.CompletedTask; },
            limits: new(Tree: tree, RecoveryTree: tree, MaximumChanges: 1), cancellation: cancellation).ConfigureAwait(false);
        return new(result.Result, previous, result.Applied, result.Diagnostics);
    }

    private static string NuLiteral(string path)
    {
        var value = path.Replace('\\', '/'); var hashes = "#";
        while (value.Contains("'" + hashes, StringComparison.Ordinal)) hashes += "#";
        return "r" + hashes + "'" + value + "'" + hashes;
    }

    private static void Write(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(flushToDisk: true);
    }

    private static void Validate(NativeControlGeneration value, string path)
    {
        if (value.SchemaVersion != 1 || !Guid.TryParseExact(value.Generation, "N", out _) || value.Current is null || value.Retired.IsDefault || value.Retired.Length > 128)
            throw Error("InvalidControl", "Installation control identity is invalid", path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in value.Retired.Prepend(value.Current))
        {
            if (reference is null || !NativePackageManifest.HashValue(reference.BuildId) || reference.BuildId != reference.BuildId.ToUpperInvariant()
                || !NativePackageManifest.HashValue(reference.ArchiveSha256) || reference.FileIdentity is not { Length: 48 } || !reference.FileIdentity.All(Uri.IsHexDigit)
                || string.IsNullOrWhiteSpace(reference.Version) || reference.Version.Length > 64
                || reference.Version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '+') || !seen.Add(reference.BuildId))
                throw Error("InvalidControl", "Installation version reference is invalid or duplicated", path);
        }
        if (value.PreviousBuildId is not null && !value.Retired.Any(reference => reference.BuildId == value.PreviousBuildId))
            throw Error("InvalidControl", "Previous installation version is not in the retirement list", path);
    }

    private static void VerifyTopology(string root)
    {
        using var identity = WindowsProjectIdentity.Open(root); identity.AssertDefaultStreamOnly();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            WindowsProjectIdentity.GuardAncestors(entry); var name = Path.GetFileName(entry);
            if (name == "current.json" && File.Exists(entry)) continue;
            if (name != "Nushell" || !Directory.Exists(entry)) throw Error("InvalidControl", "Installation control contains unknown contents", entry);
            using var child = WindowsProjectIdentity.Open(entry); child.AssertDefaultStreamOnly();
            foreach (var leaf in Directory.EnumerateFileSystemEntries(entry))
            {
                WindowsProjectIdentity.GuardAncestors(leaf);
                if (Path.GetFileName(leaf) != "modpack.nu" || !File.Exists(leaf)) throw Error("InvalidControl", "Installation control contains unknown contents", leaf);
                using var stream = new FileStream(leaf, FileMode.Open, FileAccess.Read, FileShare.Read);
                WindowsProjectIdentity.AssertStreamBytes(stream.SafeFileHandle, MaximumControlBytes, leaf, defaultStreamOnly: true);
            }
            child.AssertCurrent();
        }
        identity.AssertCurrent();
    }

    private static void ReferenceFields(JsonElement value) => Fields(value, "build_id", "version", "file_identity", "archive_sha256");
    private static void Fields(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("InvalidControl", "Installation control field must be an object", "current.json");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!expected.Contains(field.Name, StringComparer.Ordinal) || !names.Add(field.Name)) throw Error("InvalidControl", "Installation control has an unknown or duplicate field", field.Name);
        if (names.Count != expected.Length) throw Error("InvalidControl", "Installation control is missing a required field", "current.json");
    }

    private static DomainException Error(string id, string message, string path, string? details = null) =>
        new("Installation." + id, message, "inspect the managed installation before retrying", "InvalidData", path, details: details);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(NativeControlGeneration))]
internal sealed partial class NativeControlJson : JsonSerializerContext;
