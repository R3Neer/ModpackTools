using System.Text.Json;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

internal static class NativeInstallationSmoke
{
    public static async Task VerifyAsync(NativePreparedPackage package, string nu, string host,
        CancellationToken cancellation = default)
    {
        NativePackageArchive.Verify(package, cancellation: cancellation);
        foreach (var name in new[] { "modpack.exe", "modpacktools-setup.exe" })
        {
            var flag = name == "modpack.exe" ? "--launcher-protocol" : "--setup-protocol";
            var result = await NativeTool.RunAsync(Path.Combine(package.Directory, name), [flag], package.Directory, host,
                new(10000, 65536), cancellation).ConfigureAwait(false);
            if (result.Stdout.Trim() != "1" || result.Stderr.Length != 0)
                throw Error("ProtocolMismatch", "Native package component protocol does not match this installer", Path.Combine(package.Directory, name));
        }
        var version = await NativeTool.RunAsync(Path.Combine(package.Directory, "modpacktools.exe"), ["--version"], package.Directory, host,
            new(10000, 65536), cancellation).ConfigureAwait(false);
        using (var document = JsonDocument.Parse(version.Stdout, new() { MaxDepth = 8 }))
        {
            var root = document.RootElement;
            if (!root.GetProperty("ok").GetBoolean() || root.GetProperty("schema_version").GetInt32() != 1
                || root.GetProperty("data").GetProperty("version").GetProperty("version").GetString() != package.Manifest.Version || version.Stderr.Length != 0)
                throw Error("ProtocolMismatch", "Native core version does not match the package manifest", package.Directory);
        }
        await WithPrivateContext(nu, package.Directory, host, async (environment, config, token) =>
        {
            var result = await NativeTool.RunAsync(nu, ["--no-config-file", "--config-home", config, Path.Combine(package.Directory, "cli.nu"), "content", "--help", "--ascii", "--color", "never", "--width", "80"],
                package.Directory, host, new(30000, 256 * 1024), token, environment).ConfigureAwait(false);
            if (!IsContentHelp(result.Stdout, result.Stderr))
                throw Error("HumanSmokeFailed", "The verified Nu/R3CLI package did not render its native help", package.Directory);
        }, cancellation).ConfigureAwait(false);
        NativePackageArchive.Verify(package, cancellation: cancellation);
    }

    public static Task VerifyBootstrapAsync(string bootstrap, string nu, string host, CancellationToken cancellation) =>
        WithPrivateContext(nu, Path.GetDirectoryName(bootstrap)!, host, async (environment, config, token) =>
        {
            var path = bootstrap.Replace('\\', '/'); var hashes = "#";
            while (path.Contains("'" + hashes, StringComparison.Ordinal)) hashes += "#";
            var script = "use r" + hashes + "'" + path + "'" + hashes + " *; modpack content --help --ascii --color never --width 80";
            var result = await NativeTool.RunAsync(nu, ["--no-config-file", "--config-home", config, "-c", script], Path.GetDirectoryName(bootstrap)!, host,
                new(30000, 256 * 1024), token, environment).ConfigureAwait(false);
            if (!IsContentHelp(result.Stdout, result.Stderr))
                throw Error("HumanSmokeFailed", "The prepared public Nu bootstrap did not render native help", bootstrap);
        }, cancellation);

    private static async Task WithPrivateContext(string nu, string directory, string host,
        Func<IReadOnlyDictionary<string, string?>, string, CancellationToken, Task> action, CancellationToken cancellation)
    {
        WindowsProjectIdentity.GuardAncestors(nu);
        var nuVersion = await NativeTool.RunAsync(nu, ["--version"], directory, host, new(10000, 65536), cancellation).ConfigureAwait(false);
        if (!Version.TryParse(nuVersion.Stdout.Trim(), out var parsed) || parsed < new Version(0, 116, 1))
            throw Error("NuUnsupported", "Native integration requires Nushell 0.116.1 or newer", nu);
        var root = Path.Combine(Path.GetTempPath(), ".modpacktools-smoke-" + Guid.NewGuid().ToString("N"));
        WindowsProjectIdentity.GuardAncestors(root);
        if (!WindowsDirectoryReservation.TryCreate(root)) throw Error("SmokeStageCreateFailed", "Private smoke configuration could not be reserved", root);
        using var owner = WindowsProjectIdentity.Open(root);
        var children = new Dictionary<string, ProjectPhysicalIdentity>(StringComparer.Ordinal);
        var names = new[] { "configuration", "state", "Roaming", "Local", "nu-config" };
        var environment = new Dictionary<string, string?>
        {
            ["MODPACKTOOLS_CONFIG_HOME"] = Path.Combine(root, "configuration"),
            ["MODPACKTOOLS_STATE_HOME"] = Path.Combine(root, "state"),
            ["APPDATA"] = Path.Combine(root, "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(root, "Local"),
            ["MODPACKTOOLS_PROJECT"] = null
        };
        Exception? failure = null;
        try
        {
            foreach (var name in names)
            {
                var path = Path.Combine(root, name);
                if (!WindowsDirectoryReservation.TryCreate(path)) throw new IOException("Private smoke directory could not be exclusively reserved: " + path);
                children.Add(name, WindowsProjectIdentity.Inspect(path));
            }
            await action(environment, Path.Combine(root, "nu-config"), cancellation).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        if (failure?.Data["ModpackTools.RetainStage"] is true)
        {
            var retained = Error("SmokeStageRetained", "Native process ownership is uncertain; preserve its private smoke configuration", root, failure.Message);
            retained.Data["ModpackTools.RetainStage"] = true; throw retained;
        }
        try
        {
            owner.AssertCurrent();
            using (var identity = WindowsProjectIdentity.Open(root))
            {
                if (identity.Value != owner.Value) throw new IOException("Private smoke directory identity changed");
                identity.AssertDefaultStreamOnly();
                var expected = names.ToHashSet(StringComparer.Ordinal);
                foreach (var path in Directory.EnumerateFileSystemEntries(root))
                {
                    WindowsProjectIdentity.GuardAncestors(path);
                    if (!Directory.Exists(path) || !expected.Remove(Path.GetFileName(path)) || Directory.EnumerateFileSystemEntries(path).Any())
                        throw new IOException("Private smoke configuration contains unexpected outputs");
                    using var child = WindowsProjectIdentity.Open(path); child.AssertDefaultStreamOnly();
                    if (!children.TryGetValue(Path.GetFileName(path), out var expectedChild) || child.Value != expectedChild)
                        throw new IOException("Private smoke child directory identity changed: " + path);
                }
                if (expected.Count != 0) throw new IOException("Private smoke configuration layout changed");
                foreach (var name in names) Directory.Delete(Path.Combine(root, name), recursive: false);
                identity.AssertCurrent();
            }
            owner.Dispose(); Directory.Delete(root, recursive: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DomainException)
        {
            var retained = Error("SmokeStageRetained", "Inspect the retained private smoke configuration", root, failure?.Message + "; " + error.Message);
            retained.Data["ModpackTools.RetainStage"] = true; throw retained;
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static bool IsContentHelp(string stdout, string stderr)
    {
        var text = stdout + stderr;
        return text.Contains("CONTENT", StringComparison.Ordinal) && text.Contains("USAGE", StringComparison.Ordinal)
            && text.Contains("modpack content list", StringComparison.Ordinal) && !text.Contains('\x1b');
    }

    private static DomainException Error(string id, string message, string path, string? details = null) => NativeInstallationRoot.Error(id, message, path, details);
}
