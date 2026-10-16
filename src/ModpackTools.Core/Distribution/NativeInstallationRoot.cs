using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

public sealed record NativeInstallationOwner(int SchemaVersion, string InstallId, ProjectPhysicalIdentity Root,
    string NuExecutable, int BootstrapProtocol);

public static class NativeInstallationRoot
{
    public static NativeInstallationOwner Create(string root, string nuExecutable)
    {
        root = Path.GetFullPath(root); nuExecutable = Path.GetFullPath(nuExecutable);
        ValidateEncoding(root); ValidateEncoding(nuExecutable); WindowsProjectIdentity.GuardAncestors(root); WindowsProjectIdentity.GuardAncestors(nuExecutable);
        if (!File.Exists(nuExecutable)) throw Error("NuMissing", "Select the existing native Nushell executable", nuExecutable);
        if (!Directory.Exists(Path.GetDirectoryName(root))) throw Error("ParentMissing", "Create or select the explicit application parent directory first", root);
        if (!WindowsDirectoryReservation.TryCreate(root)) throw Error("RootOccupied", "A new installation requires an unused explicit application directory", root);
        try
        {
            using var identity = WindowsProjectIdentity.Open(root); ValidateEncoding(identity.Value.CanonicalRoot);
            var owner = new NativeInstallationOwner(1, Guid.NewGuid().ToString("N"), identity.Value, nuExecutable, 1);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(owner, NativeRootJson.Default.NativeInstallationOwner);
            using (var output = new FileStream(Path.Combine(root, "installation.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes); output.Flush(flushToDisk: true); }
            foreach (var name in new[] { "bin", "versions", "control", "pins" })
                if (!WindowsDirectoryReservation.TryCreate(Path.Combine(root, name))) throw Error("RootChanged", "Installation layout changed while it was initialized", root);
            identity.AssertCurrent(); return owner;
        }
        catch (Exception error)
        {
            var retained = Error("RootRetained", "Installation initialization failed; inspect the retained application directory", root, error.Message);
            retained.Data["ModpackTools.Installation.RootCreated"] = true;
            retained.Data["ModpackTools.RetainStage"] = true; throw retained;
        }
    }

    public static NativeInstallationOwner Read(string root)
    {
        using var identity = WindowsProjectIdentity.Open(root); identity.AssertDefaultStreamOnly();
        var path = Path.Combine(identity.Value.CanonicalRoot, "installation.json"); WindowsProjectIdentity.GuardAncestors(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        WindowsProjectIdentity.AssertStreamBytes(stream.SafeFileHandle, 32768, path, defaultStreamOnly: true);
        if (stream.Length > 32768) throw Error("InvalidOwner", "Installation owner exceeds its byte quota", path);
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 }); var value = document.RootElement;
            Fields(value, "schema_version", "install_id", "root", "nu_executable", "bootstrap_protocol");
            Fields(value.GetProperty("root"), "canonical_root", "file_identity");
            var owner = JsonSerializer.Deserialize(bytes, NativeRootJson.Default.NativeInstallationOwner);
            if (owner is null || owner.SchemaVersion != 1 || owner.BootstrapProtocol != 1 || !Guid.TryParseExact(owner.InstallId, "N", out _)
                || owner.Root != identity.Value || string.IsNullOrWhiteSpace(owner.NuExecutable) || !Path.IsPathFullyQualified(owner.NuExecutable))
                throw Error("InvalidOwner", "Installation ownership or physical identity is invalid", path);
            ValidateEncoding(owner.NuExecutable); ValidateTopology(identity.Value.CanonicalRoot); identity.AssertCurrent(); return owner;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { throw Error("InvalidOwner", "Installation owner JSON is invalid", path, error.Message); }
    }

    public static async Task<NativeInstallationLease> AcquireAsync(string root)
    {
        var owner = Read(root); var identity = WindowsProjectIdentity.Open(owner.Root.CanonicalRoot);
        NamedProjectLease? lease = null;
        try
        {
            if (identity.Value != owner.Root) throw Error("RootChanged", "Installation directory identity changed", root);
            lease = await NamedProjectLease.AcquireAsync(owner.Root.FileIdentity).ConfigureAwait(false);
            if (Read(root) != owner) throw Error("RootChanged", "Installation owner changed before admission", root);
            return new(owner, identity, lease);
        }
        catch { if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false); identity.Dispose(); throw; }
    }

    private static void ValidateTopology(string root)
    {
        var expected = new HashSet<string>(["installation.json", "bin", "versions", "control", "pins"], StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            WindowsProjectIdentity.GuardAncestors(entry); var name = Path.GetFileName(entry);
            if (!expected.Remove(name) || (File.GetAttributes(entry) & FileAttributes.Directory) != (name == "installation.json" ? 0 : FileAttributes.Directory))
                throw Error("InvalidOwner", "Installation root contains unknown contents or topology", entry);
        }
        if (expected.Count != 0) throw Error("InvalidOwner", "Installation root is missing required layout entries", root);
    }

    private static void ValidateEncoding(string path)
    {
        try { _ = new UTF8Encoding(false, true).GetByteCount(path); }
        catch (EncoderFallbackException error) { throw Error("InvalidPath", "Installation paths must be valid Unicode", path, error.Message); }
    }

    private static void Fields(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("InvalidOwner", "Installation owner field must be an object", "installation.json");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!expected.Contains(field.Name, StringComparer.Ordinal) || !names.Add(field.Name)) throw Error("InvalidOwner", "Installation owner has an unknown or duplicate field", field.Name);
        if (names.Count != expected.Length) throw Error("InvalidOwner", "Installation owner is missing a required field", "installation.json");
    }

    internal static DomainException Error(string id, string message, string path, string? details = null) =>
        new("Installation." + id, message, "use the explicit managed application directory; preserve sources and configuration", "InvalidData", path, details: details);
}

public sealed class NativeInstallationLease : IAsyncDisposable
{
    private readonly WindowsProjectIdentity identity;
    private readonly NamedProjectLease lease;
    private int disposed;
    public NativeInstallationOwner Owner { get; }
    internal NativeInstallationLease(NativeInstallationOwner owner, WindowsProjectIdentity identity, NamedProjectLease lease)
    { Owner = owner; this.identity = identity; this.lease = lease; }
    public void AssertCurrent()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        identity.AssertCurrent();
        if (NativeInstallationRoot.Read(Owner.Root.CanonicalRoot) != Owner) throw NativeInstallationRoot.Error("RootChanged", "Installation owner changed during the operation", Owner.Root.CanonicalRoot);
    }
    public NativeLaunchPin PinCurrent()
    {
        AssertCurrent(); var root = Owner.Root.CanonicalRoot;
        var current = NativeInstallationControl.Read(Path.Combine(root, "control"))?.Current
            ?? throw NativeInstallationRoot.Error("NotReady", "Installation has no active version", root);
        return Pin(current);
    }
    public NativeLaunchPin PinVersion(string buildId)
    {
        AssertCurrent(); var root = Owner.Root.CanonicalRoot;
        var generation = NativeInstallationControl.Read(Path.Combine(root, "control"))
            ?? throw NativeInstallationRoot.Error("NotReady", "Installation has no active version", root);
        var selected = generation.Retired.Prepend(generation.Current).SingleOrDefault(value => value.BuildId == buildId)
            ?? throw NativeInstallationRoot.Error("UnknownVersion", "Requested version is not managed by this installation", buildId);
        return Pin(selected);
    }
    private NativeLaunchPin Pin(NativeVersionReference current)
    {
        var root = Owner.Root.CanonicalRoot;
        var version = Path.Combine(root, "versions", current.BuildId);
        using var versionIdentity = WindowsProjectIdentity.Open(version);
        if (versionIdentity.Value.FileIdentity != current.FileIdentity) throw NativeInstallationRoot.Error("VersionChanged", "Active version directory identity changed", version);
        _ = NativePackageArchive.OpenInstalled(version, current);
        var path = Path.Combine(root, "pins", current.BuildId + ".lease"); WindowsProjectIdentity.GuardAncestors(path);
        if (!File.Exists(path))
        { using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); created.Flush(flushToDisk: true); }
        var pin = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            WindowsProjectIdentity.AssertStreamBytes(pin.SafeFileHandle, 0, path, defaultStreamOnly: true);
            if (pin.Length != 0) throw NativeInstallationRoot.Error("InvalidPin", "Version admission pin must be empty", path);
            AssertCurrent(); versionIdentity.AssertCurrent(); return new(current, versionIdentity.Value, Owner.NuExecutable, pin);
        }
        catch { pin.Dispose(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { await lease.DisposeAsync().ConfigureAwait(false); } finally { identity.Dispose(); }
    }
}

public sealed class NativeLaunchPin : IDisposable
{
    private readonly FileStream pin;
    public NativeVersionReference Version { get; }
    public ProjectPhysicalIdentity Identity { get; }
    public string Directory => Identity.CanonicalRoot;
    public string NuExecutable { get; }
    internal NativeLaunchPin(NativeVersionReference version, ProjectPhysicalIdentity identity, string nuExecutable, FileStream pin)
    { Version = version; Identity = identity; NuExecutable = nuExecutable; this.pin = pin; }
    public void Dispose() => pin.Dispose();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(NativeInstallationOwner))]
internal sealed partial class NativeRootJson : JsonSerializerContext;
