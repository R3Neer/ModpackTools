using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core;
using ModpackTools.Core.Distribution;
using ModpackTools.Core.Versions;

namespace ModpackTools.Cli;

internal sealed record SelfUpdatePresentation(string Kind, string Status, string CurrentVersion, string TargetVersion,
    string Root, string Source, bool NativeAvailable, string Notice, bool NeedsConfirmation, string? ReviewToken,
    NativeInstallationResult? Installation);
internal sealed record SelfUpdateEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, EmptyData Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SelfUpdatePresentation? Presentation = null);

internal static class SelfUpdateCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? reviewToken,
        bool human, CancellationToken cancellation)
    {
        var options = Parse(arguments);
        if (requestedProject is not null) throw new DomainException("Option.ForbiddenCombination", "Self-update does not accept a project selection", "modpack self-update --help", target: requestedProject);
        var host = Environment.ProcessPath ?? throw Invalid("Native executable location is unavailable");
        var directory = Path.GetDirectoryName(host)!; var parent = Path.GetDirectoryName(directory);
        var build = Path.GetFileName(directory);
        if (!Path.GetFileName(host).Equals("modpacktools.exe", StringComparison.OrdinalIgnoreCase) || parent is null
            || !Path.GetFileName(parent).Equals("versions", StringComparison.OrdinalIgnoreCase) || build.Length != 64 || !build.All(Uri.IsHexDigit))
            throw new DomainException("SelfUpdate.NotManaged", "This executable is not in a managed native installation", "install a verified native package with modpacktools-setup install --root DIRECTORY first", "ObjectNotFound", host);
        var root = Path.GetDirectoryName(parent)!; NativeControlGeneration current; NativeLaunchPin ownPin; NativeInstallationOwner owner;
        await using (var admission = await NativeInstallationRoot.AcquireAsync(root).ConfigureAwait(false))
        {
            root = admission.Owner.Root.CanonicalRoot;
            owner = admission.Owner;
            ownPin = admission.PinVersion(build);
            try { current = NativeInstallationControl.Read(Path.Combine(root, "control")) ?? throw Invalid("Native installation is not active"); }
            catch { ownPin.Dispose(); throw; }
        }
        using (ownPin)
        {
            var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
            NativeRelease? release = null; string targetVersion, source, sha; string? targetBuild;
            var available = true; var notice = ""; var upToDate = false;
            if (options.Package is not null)
            {
                source = Path.GetFullPath(options.Package);
                var package = InspectLocal(root, state, source, options.Sha256!, cancellation);
                targetVersion = package.Version; sha = package.Sha256; targetBuild = package.BuildId;
                var order = VersionOrdering.Compare(targetVersion, current.Current.Version);
                if (order is null) throw Invalid("Selected package version cannot be compared with the installation");
                if (order < 0 && !options.AllowDowngrade) throw new DomainException("SelfUpdate.DowngradeRefused", "An older local package requires --allow-downgrade", "review the package before allowing a downgrade", target: source);
                if (targetBuild == current.Current.BuildId) { notice = "The selected package is already active."; upToDate = true; }
            }
            else
            {
                release = await NativeReleaseChannel.LatestAsync(cancellation).ConfigureAwait(false);
                targetVersion = release.Version; source = release.Url; sha = release.Asset?.Sha256 ?? ""; targetBuild = null;
                available = release.Asset is not null;
                var order = VersionOrdering.Compare(targetVersion, current.Current.Version);
                if (order is null) throw Invalid("Stable release version cannot be compared with the installation");
                if (order <= 0) { notice = "No newer stable release is available."; upToDate = true; }
                else if (!available) notice = release.UnavailableReason!;
            }
            var token = Review(current, owner, source, sha, targetVersion, targetBuild, options.AllowDowngrade);
            if (reviewToken is not null && !StringComparer.OrdinalIgnoreCase.Equals(token, reviewToken))
                throw new DomainException("SelfUpdate.ReviewChanged", "The selected update or installation changed after review", "review self-update again before confirming", "ResourceBusy", root);
            var needsConfirmation = !upToDate && available;
            if (!options.Yes || options.Check || !needsConfirmation)
            {
                if (options.Yes && !available && !upToDate)
                    throw new DomainException("SelfUpdate.MissingNativeAsset", notice, "wait for an official native release or select a verified local native package", "ObjectNotFound", source);
                Emit(new("self-update", upToDate ? "current" : needsConfirmation ? "available" : "unavailable", current.Current.Version,
                    targetVersion, root, source, available, notice, needsConfirmation, needsConfirmation ? token : null, null)); return;
            }
            NativeInstallationResult installed;
            try
            {
                installed = release is null
                    ? await NativeInstallation.ApplyAsync(source, sha, root, null, state, host, install: false,
                        allowDowngrade: options.AllowDowngrade, cancellation: cancellation, expectedGeneration: current.Generation,
                        expectedVersion: targetVersion).ConfigureAwait(false)
                    : await NativeReleaseUpdate.ApplyAsync(root, state, host, release, current.Generation, cancellation).ConfigureAwait(false);
            }
            catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
            {
                var domain = error as DomainException;
                var details = (domain?.Details ?? "") + "; application_root=" + root
                    + "; prepared_directory=" + error.Data["ModpackTools.Installation.PreparedDirectory"]
                    + "; version_published=" + (error.Data["ModpackTools.Installation.VersionPublished"] is true)
                    + "; bin_ready=" + (error.Data["ModpackTools.Installation.BinReady"] is true)
                    + "; retain_stage=" + (error.Data["ModpackTools.RetainStage"] is true);
                var cancelled = error is OperationCanceledException || domain?.Id == "ModpackTools.Core.OperationCancelled";
                var wrapped = new DomainException(cancelled ? "Core.OperationCancelled" : domain?.Id["ModpackTools.".Length..] ?? "SelfUpdate.ApplyFailed", error.Message, "inspect the reported installation and retained paths before retrying",
                    cancelled ? "OperationStopped" : domain?.Category ?? "WriteError", targetData: domain?.Target ?? new TextTarget(root), details: (domain is null ? "" : domain.Id + "; ") + details);
                foreach (var key in error.Data.Keys) wrapped.Data[key] = error.Data[key]; throw wrapped;
            }
            Emit(new("self-update", installed.ControlSwitched ? "updated" : "current", current.Current.Version, installed.Version,
                root, source, true, installed.ControlSwitched ? "Open a new Nushell session to load the installed version." : "The selected package is already active.", false, null, installed));
        }

        void Emit(SelfUpdatePresentation presentation)
        {
            var envelope = new SelfUpdateEnvelope(1, true, command, arguments, new(), human ? presentation : null);
            using var output = new NativeOutputBuffer("Self-update result", "inspect the native installation directly");
            using (var writer = new Utf8JsonWriter(output)) JsonSerializer.Serialize(writer, envelope, WireJsonContext.Default.SelfUpdateEnvelope);
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
        }
    }

    private sealed record LocalPackage(string Version, string BuildId, string Sha256);
    private static LocalPackage InspectLocal(string root, string state, string path, string sha, CancellationToken cancellation)
    {
        var package = NativePackageArchive.Prepare(path, sha, NativeInstallation.GetPreparationParent(root, state), cancellation: cancellation);
        try { return new(package.Manifest.Version, package.Manifest.BuildId, package.ArchiveSha256); }
        finally
        {
            try { NativeInstallation.ReleasePreparedPackage(package); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
            {
                var retained = new DomainException("SelfUpdate.PreparationRetained", "Preserve the verified package preparation for inspection",
                    "inspect the retained path before retrying", "WriteError", package.Directory, details: cleanup.Message);
                retained.Data["ModpackTools.RetainStage"] = true; throw retained;
            }
        }
    }

    private sealed record Options(bool Check, bool Yes, bool AllowDowngrade, string? Package, string? Sha256);
    private static Options Parse(string[] arguments)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Length; index++)
        {
            var pieces = arguments[index].Split('=', 2); var name = pieces[0];
            if (!values.TryAdd(name, null)) throw new DomainException("Option.Duplicate", "Self-update option is repeated: " + name, "specify each option once", target: name);
            if (name is "--check" or "--yes" or "--allow-downgrade")
            { if (pieces.Length != 1) throw Invalid("Switch does not accept a value: " + name); }
            else if (name is "--package" or "--sha256")
            {
                var value = pieces.Length == 2 ? pieces[1] : ++index < arguments.Length ? arguments[index] : "";
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal)) throw Invalid("Missing value for " + name);
                values[name] = value;
            }
            else throw new DomainException("Option.Unknown", "Self-update option is not recognized: " + name, "modpack self-update --help", target: name);
        }
        if (values.ContainsKey("--check") && values.ContainsKey("--yes")) throw Invalid("Use either --check or --yes");
        if (values.ContainsKey("--package") != values.ContainsKey("--sha256")) throw Invalid("A local package requires both --package and --sha256");
        if (values.ContainsKey("--allow-downgrade") && !values.ContainsKey("--package")) throw Invalid("Allow-downgrade requires an explicit local package");
        return new(values.ContainsKey("--check"), values.ContainsKey("--yes"), values.ContainsKey("--allow-downgrade"), values.GetValueOrDefault("--package"), values.GetValueOrDefault("--sha256"));
    }

    private static string Review(NativeControlGeneration control, NativeInstallationOwner owner, string source, string sha, string version, string? build, bool downgrade)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject(); writer.WriteString("generation", control.Generation); writer.WriteString("root", owner.Root.CanonicalRoot);
            writer.WriteString("install_id", owner.InstallId); writer.WriteString("root_identity", owner.Root.FileIdentity);
            writer.WriteString("current_identity", control.Current.FileIdentity); writer.WriteString("source", source);
            writer.WriteString("sha256", sha); writer.WriteString("version", version); writer.WriteString("build", build);
            writer.WriteBoolean("allow_downgrade", downgrade); writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(output.ToArray()));
    }

    private static DomainException Invalid(string message) => new("SelfUpdate.InvalidArguments", message, "modpack self-update --help");
}
