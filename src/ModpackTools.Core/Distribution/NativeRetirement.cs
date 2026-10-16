using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

public sealed record NativeRetirementItem(string BuildId, string Status, string Path, string? Reason);
public sealed record NativeRetirementResult(string Root, ImmutableArray<NativeRetirementItem> Items);
internal sealed record NativeRetirementJournal(int SchemaVersion, string InstallId, string RootIdentity, string Generation,
    NativeVersionReference Version, string Tombstone, string Phase, string Manifest, ImmutableArray<ProjectPhysicalIdentity> Directories);

public static class NativeRetirement
{
    internal static string JournalPath(string root, string build) => Path.Combine(root, "pins", "retire-" + build + ".json");
    internal static void AssertAvailable(string root, string build)
    {
        if (File.Exists(JournalPath(root, build))) throw Error("VersionRetiring", "Version retirement must finish before selecting this build again", Path.Combine(root, "versions", build));
    }

    public static async Task<NativeRetirementResult> CollectAsync(string root, string stateDirectory,
        CancellationToken cancellation = default, Action<NativePackageProgress>? progress = null)
    {
        root = FileTreeTransaction.CanonicalFutureDirectory(root); stateDirectory = FileTreeTransaction.CanonicalFutureDirectory(stateDirectory);
        if (root.Equals(stateDirectory, StringComparison.OrdinalIgnoreCase) || TransactionFiles.IsBelow(root, stateDirectory) || TransactionFiles.IsBelow(stateDirectory, root))
            throw Error("UnsafeState", "Application and recovery-state directories must be separate trees", stateDirectory);
        await using var admission = await NativeInstallationRoot.AcquireAsync(root).ConfigureAwait(false);
        using var versionsOwner = WindowsProjectIdentity.Open(Path.Combine(root, "versions")); versionsOwner.AssertDefaultStreamOnly();
        using var pinsOwner = WindowsProjectIdentity.Open(Path.Combine(root, "pins")); pinsOwner.AssertDefaultStreamOnly();
        var control = NativeInstallationControl.Read(Path.Combine(root, "control")) ?? throw Error("NotReady", "Installation has no active version", root);
        var results = ImmutableArray.CreateBuilder<NativeRetirementItem>();
        // A bounded fixed list prevents newly discovered names from becoming deletion authority.
        var pending = Directory.EnumerateFiles(Path.Combine(root, "pins"), "retire-*.json").Take(129).ToArray();
        if (pending.Length > 128) throw Error("RetirementLimit", "Retirement journal count exceeds its quota", Path.Combine(root, "pins"));
        var targets = pending.Select(path => Path.GetFileName(path)[7..^5]).Concat(control.Retired.Select(value => value.BuildId)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var build in targets)
        {
            cancellation.ThrowIfCancellationRequested(); var directory = Path.Combine(root, "versions", build);
            try
            {
                if (!NativePackageManifest.HashValue(build) || build != build.ToUpperInvariant()) throw Error("InvalidRetirement", "Unknown retirement journal name", directory);
                admission.AssertCurrent();
                control = NativeInstallationControl.Read(Path.Combine(root, "control")) ?? throw Error("NotReady", "Installation has no active version", root);
                if (build == control.Current.BuildId) throw Error("ActiveVersion", "The active version cannot be retired", directory);
                var reason = NativeRetirementCensus.DeferredReason(admission.Owner, control);
                if (reason is not null) { results.Add(new(build, "deferred", directory, reason)); continue; }
                var pinPath = Path.Combine(root, "pins", build + ".lease"); WindowsProjectIdentity.GuardAncestors(pinPath);
                // Admission excludes new pins; exclusive access excludes already admitted children.
                using var pin = new FileStream(pinPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                WindowsProjectIdentity.AssertStreamBytes(pin.SafeFileHandle, 0, pinPath, defaultStreamOnly: true);
                if (pin.Length != 0) throw Error("InvalidPin", "Version pin contains unexpected data", pinPath);
                var journalPath = JournalPath(root, build); NativeRetirementJournal journal;
                if (File.Exists(journalPath)) journal = ReadJournal(journalPath, admission.Owner, build);
                else
                {
                    var reference = control.Retired.Single(value => value.BuildId == build);
                    var package = NativePackageArchive.OpenInstalled(directory, reference, cancellation);
                    var children = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var file in package.Files)
                        for (var slash = file.Path.IndexOf('/'); slash >= 0; slash = file.Path.IndexOf('/', slash + 1)) children.Add(file.Path[..slash]);
                    var identities = children.Order(StringComparer.Ordinal).Select(relative => WindowsProjectIdentity.Inspect(Path.Combine(directory, relative))).ToImmutableArray();
                    journal = new(1, admission.Owner.InstallId, admission.Owner.Root.FileIdentity, control.Generation, reference,
                        ".retiring-" + Guid.NewGuid().ToString("N"), "prepared", Convert.ToBase64String(package.Manifest.ToBytes()), identities);
                    // Check open images/handles before making retirement durable.
                    VerifyRemaining(directory, journal, original: true, cancellation);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, NativeRetirementJournalJson.Default.NativeRetirementJournal);
                    if (bytes.Length > 128 * 1024) throw Error("RetirementLimit", "Retirement journal exceeds its resumable metadata quota", journalPath);
                    using var output = new FileStream(journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    output.Write(bytes); output.Flush(flushToDisk: true);
                }
                var retired = Path.Combine(root, "versions", journal.Tombstone);
                var managedReference = control.Retired.SingleOrDefault(value => value.BuildId == build);
                if (managedReference != journal.Version && (managedReference is not null || journal.Phase != "empty" || Directory.Exists(directory) || Directory.Exists(retired)))
                    throw Error("RetirementChanged", "Retirement is no longer bound to the recorded managed version", journalPath);
                if (Directory.Exists(directory))
                {
                    if (journal.Phase != "prepared") throw Error("RetirementChanged", "An emptied retirement unexpectedly has an original version tree", directory);
                    if (Directory.Exists(retired)) throw Error("RetirementChanged", "Both original and retired version paths exist", retired);
                    VerifyRemaining(directory, journal, original: true, cancellation);
                    Directory.Move(directory, retired);
                }
                if (Directory.Exists(retired))
                {
                    using var protectedRetirement = WindowsProjectIdentity.Open(retired);
                    if (protectedRetirement.Value.FileIdentity != journal.Version.FileIdentity) throw Error("RetirementChanged", "Retirement identity changed", retired);
                    var files = VerifyRemaining(retired, journal, original: false, cancellation);
                    var lengths = files.ToDictionary(path => path, path => new FileInfo(path).Length, StringComparer.Ordinal);
                    var totalBytes = lengths.Values.Sum(); long completedBytes = 0; var completedFiles = 0;
                    foreach (var file in files.OrderBy(path => Path.GetFileName(path) == "package.json" ? 1 : 0))
                    {
                        cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(file); File.Delete(file);
                        completedBytes += lengths[file]; completedFiles++;
                        progress?.Invoke(new("retire", file, completedBytes, totalBytes, completedFiles, files.Count));
                    }
                    foreach (var child in journal.Directories.OrderByDescending(value => value.CanonicalRoot.Length))
                    {
                        var relative = Path.GetRelativePath(directory, child.CanonicalRoot); var path = Path.Combine(retired, relative);
                        if (Directory.Exists(path)) Directory.Delete(path, recursive: false);
                    }
                    if (journal.Phase != "empty")
                    {
                        var previous = JsonSerializer.SerializeToUtf8Bytes(journal, NativeRetirementJournalJson.Default.NativeRetirementJournal);
                        journal = journal with { Phase = "empty" };
                        var completed = JsonSerializer.SerializeToUtf8Bytes(journal, NativeRetirementJournalJson.Default.NativeRetirementJournal);
                        if (completed.Length > 128 * 1024) throw Error("RetirementLimit", "Completed retirement journal exceeds its metadata quota", journalPath);
                        NativeRetirementCensus.WriteOwned(journalPath, completed, previous);
                    }
                    protectedRetirement.AssertCurrent(); protectedRetirement.Dispose();
                    Directory.Delete(retired, recursive: false);
                }
                else if (journal.Phase != "empty") throw Error("RetirementChanged", "Retirement tree is missing without a durable empty-tree receipt", retired);
                if (control.Retired.Any(value => value.BuildId == build))
                {
                    if (control.Retired.Single(value => value.BuildId == build) != journal.Version) throw Error("RetirementChanged", "Retired reference changed since journal creation", journalPath);
                    var pruned = await NativeInstallationControl.PruneAsync(Path.Combine(root, "control"), stateDirectory, control.Generation, build, cancellation).ConfigureAwait(false);
                    control = pruned.Current;
                    _ = NativeRetirementCensus.Capture(admission.Owner, control);
                }
                // Verify the durable receipt again before removing it, including resumed completions.
                var reread = ReadJournal(journalPath, admission.Owner, build);
                if (!JsonSerializer.SerializeToUtf8Bytes(reread, NativeRetirementJournalJson.Default.NativeRetirementJournal)
                    .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(journal, NativeRetirementJournalJson.Default.NativeRetirementJournal)))
                    throw Error("RetirementChanged", "Retirement journal changed during cleanup", journalPath);
                File.Delete(journalPath); results.Add(new(build, "removed", directory, null));
            }
            catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or FormatException)
            { results.Add(new(build, "retained", (error as DomainException)?.Target is TextTarget target ? target.Value : directory, error.Message)); }
        }
        return new(root, results.ToImmutable());
    }

    private static NativeRetirementJournal ReadJournal(string path, NativeInstallationOwner owner, string build)
    {
        try { return ReadJournalCore(path, owner, build); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { throw NativeInstallationRoot.Error("InvalidRetirement", "Retirement journal metadata cannot be verified", path, error.Message); }
    }

    private static NativeRetirementJournal ReadJournalCore(string path, NativeInstallationOwner owner, string build)
    {
        var bytes = NativeRetirementCensus.ReadOwned(path, 128 * 1024);
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 });
        NativeRetirementCensus.Fields(document.RootElement, "schema_version", "install_id", "root_identity", "generation", "version", "tombstone", "phase", "manifest", "directories");
        NativeRetirementCensus.Fields(document.RootElement.GetProperty("version"), "build_id", "version", "file_identity", "archive_sha256");
        foreach (var value in document.RootElement.GetProperty("directories").EnumerateArray()) NativeRetirementCensus.Fields(value, "canonical_root", "file_identity");
        var result = JsonSerializer.Deserialize(bytes, NativeRetirementJournalJson.Default.NativeRetirementJournal) ?? throw new JsonException("Missing retirement journal");
        if (result.SchemaVersion != 1 || result.InstallId != owner.InstallId || result.RootIdentity != owner.Root.FileIdentity || result.Version is null
            || result.Version.BuildId != build || result.Version.FileIdentity is not { Length: 48 } || !result.Version.FileIdentity.All(Uri.IsHexDigit)
            || !NativePackageManifest.HashValue(result.Version.ArchiveSha256) || !Guid.TryParseExact(result.Generation, "N", out _)
            || result.Tombstone is null || !result.Tombstone.StartsWith(".retiring-", StringComparison.Ordinal) || !Guid.TryParseExact(result.Tombstone[10..], "N", out _)
            || result.Phase is not ("prepared" or "empty")
            || result.Directories.IsDefault || result.Directories.Length > 16)
            throw Error("InvalidRetirement", "Retirement journal identity is invalid", path);
        var manifest = NativePackageManifest.Read(Convert.FromBase64String(result.Manifest));
        if (manifest.BuildId != build || manifest.Version != result.Version.Version) throw Error("InvalidRetirement", "Retirement manifest differs from the selected version", path);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
            for (var slash = file.Path.IndexOf('/'); slash >= 0; slash = file.Path.IndexOf('/', slash + 1)) expected.Add(Path.Combine(owner.Root.CanonicalRoot, "versions", build, file.Path[..slash].Replace('/', Path.DirectorySeparatorChar)));
        foreach (var child in result.Directories)
            if (child is null || !expected.Remove(child.CanonicalRoot) || child.FileIdentity is not { Length: 48 } || !child.FileIdentity.All(Uri.IsHexDigit))
                throw Error("InvalidRetirement", "Retirement directory identities are invalid", path);
        if (expected.Count != 0) throw Error("InvalidRetirement", "Retirement directory identities are incomplete", path);
        return result;
    }

    private static List<string> VerifyRemaining(string directory, NativeRetirementJournal journal, bool original, CancellationToken cancellation)
    {
        using var root = WindowsProjectIdentity.Open(directory); root.AssertDefaultStreamOnly();
        if (root.Value.FileIdentity != journal.Version.FileIdentity) throw Error("RetirementChanged", "Retiring version directory identity changed", directory);
        var manifest = NativePackageManifest.Read(Convert.FromBase64String(journal.Manifest)); var bytes = manifest.ToBytes();
        var known = manifest.Files.Append(new NativePackageFile("package.json", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)))).ToDictionary(value => value.Path, StringComparer.Ordinal);
        var originalRoot = Path.Combine(Path.GetDirectoryName(directory)!, journal.Version.BuildId);
        var directories = journal.Directories.ToDictionary(value => Path.GetRelativePath(originalRoot, value.CanonicalRoot).Replace('\\', '/'), StringComparer.Ordinal);
        var files = new List<string>(); var seenDirectories = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            using var identity = WindowsProjectIdentity.Open(current); identity.AssertDefaultStreamOnly();
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(path);
                var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                if (Directory.Exists(path))
                {
                    if (!directories.TryGetValue(relative, out var child) || WindowsProjectIdentity.Inspect(path).FileIdentity != child.FileIdentity)
                        throw Error("RetirementChanged", "Retiring version contains an unknown or replaced directory", path);
                    seenDirectories.Add(relative); pending.Push(path); continue;
                }
                if (!known.Remove(relative, out var file)) throw Error("RetirementChanged", "Retiring version contains an unknown file", path);
                using var input = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, file.Length, path, defaultStreamOnly: true);
                if (input.Length != file.Length || NativePackageArchive.Copy(input, Stream.Null, file.Length, cancellation) != file.Sha256)
                    throw Error("RetirementChanged", "Retiring version file changed", path);
                files.Add(path);
            }
            identity.AssertCurrent();
        }
        if (original && (known.Count != 0 || seenDirectories.Count != directories.Count)) throw Error("RetirementChanged", "Original version is incomplete before retirement", directory);
        root.AssertCurrent(); return files;
    }

    private static DomainException Error(string id, string message, string path) => NativeInstallationRoot.Error(id, message, path);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(NativeRetirementJournal))]
internal sealed partial class NativeRetirementJournalJson : JsonSerializerContext;
