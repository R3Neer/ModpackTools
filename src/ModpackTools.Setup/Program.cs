using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core;
using ModpackTools.Core.Distribution;
using ModpackTools.Core.Processes;

Console.OutputEncoding = new UTF8Encoding(false);
if (args is ["--tool-child"]) return await NativeToolChild.RunAsync().ConfigureAwait(false);
if (args is ["--setup-protocol"]) { Console.WriteLine("1"); return 0; }
var json = args.Contains("--json", StringComparer.Ordinal);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, value) => { value.Cancel = true; cancellation.Cancel(); };
string? applicationRoot = null;
try
{
    var host = Environment.ProcessPath ?? throw new InvalidOperationException("Native setup executable path is unavailable");
    var directory = Path.GetDirectoryName(host)!;
    // The stable helper delegates to the current verified helper, so protocol1
    // bootstrap does not freeze future installer fixes inside bin forever.
    if (Path.GetFileName(directory).Equals("bin", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Path.GetDirectoryName(directory)!, "installation.json")))
    {
        NativeLaunchPin? pin = null;
        await using (var admission = await NativeInstallationRoot.AcquireAsync(Path.GetDirectoryName(directory)!).ConfigureAwait(false))
            if (NativeInstallationControl.Read(Path.Combine(admission.Owner.Root.CanonicalRoot, "control")) is not null) pin = admission.PinCurrent();
        if (pin is not null)
        {
            using (pin)
            {
                var start = new ProcessStartInfo(Path.Combine(pin.Directory, "modpacktools-setup.exe")) { UseShellExecute = false };
                foreach (var argument in args) start.ArgumentList.Add(argument);
                using var child = Process.Start(start) ?? throw new IOException("Versioned native setup could not start");
                await child.WaitForExitAsync().ConfigureAwait(false); return child.ExitCode;
            }
        }
    }
    if (args is ["--version"]) { Console.WriteLine(ProductVersion.Current.Version); return 0; }
    if (args.Length == 0 || args is ["--help"])
    {
        Console.WriteLine("ModpackTools native setup\npack --core EXE --launcher EXE --assets SOURCE --output ZIP\ninstall --root NEW_DIRECTORY --nu NU_EXE --package ZIP --sha256 HASH\nupdate --root DIRECTORY --package ZIP --sha256 HASH [--allow-downgrade]\nrollback --root DIRECTORY [--build-id ID]\ncleanup --root DIRECTORY\nCommon: --state DIRECTORY --json\nNo profiles, PATH, plugins or configuration are changed automatically."); return 0;
    }
    var command = args[0]; var options = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (var index = 1; index < args.Length; index++)
    {
        var name = args[index];
        if (!name.StartsWith("--", StringComparison.Ordinal) || options.ContainsKey(name)) throw Invalid("Unknown positional or duplicate option: " + name);
        if (name is "--json" or "--allow-downgrade") options.Add(name, null);
        else
        {
            if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw Invalid("Missing value for " + name);
            options.Add(name, args[index]);
        }
    }
    var allowed = command switch
    {
        "pack" => new[] { "--core", "--launcher", "--assets", "--output", "--json" },
        "install" => ["--root", "--nu", "--package", "--sha256", "--state", "--json"],
        "update" => ["--root", "--package", "--sha256", "--state", "--allow-downgrade", "--json"],
        "rollback" => ["--root", "--build-id", "--state", "--json"],
        "cleanup" => ["--root", "--state", "--json"],
        _ => throw Invalid("Unknown native setup command: " + command)
    };
    foreach (var name in options.Keys) if (!allowed.Contains(name, StringComparer.Ordinal)) throw Invalid("Option does not apply to " + command + ": " + name);
    string Required(string name) => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw Invalid("Required option: " + name);
    var stopwatch = Stopwatch.StartNew(); long last = -1000;
    void Progress(NativePackageProgress value)
    {
        if (json || stopwatch.ElapsedMilliseconds - last < 250) return;
        last = stopwatch.ElapsedMilliseconds;
        Console.Error.WriteLine($"{value.Phase}: {value.CompletedBytes}/{value.TotalBytes} bytes, {value.CompletedFiles}/{value.TotalFiles} files — {value.Path}");
    }
    if (command == "pack")
    {
        var package = NativePackageBuilder.Build(Required("--core"), Required("--launcher"), host, Required("--assets"), Required("--output"), cancellation.Token, Progress);
        Console.WriteLine(JsonSerializer.Serialize(package, SetupJson.Default.NativePackageBuild)); return 0;
    }
    var root = Required("--root"); applicationRoot = Path.GetFullPath(root);
    var state = options.GetValueOrDefault("--state") ?? Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
    if (command == "cleanup")
    {
        var retirement = await NativeRetirement.CollectAsync(root, state, cancellation.Token, Progress).ConfigureAwait(false);
        if (json) Console.WriteLine(JsonSerializer.Serialize(retirement, SetupJson.Default.NativeRetirementResult));
        else foreach (var item in retirement.Items) Console.WriteLine($"{item.Status}: {item.Path}" + (item.Reason is null ? "" : " — " + item.Reason));
        return 0;
    }
    var result = command == "rollback"
        ? await NativeInstallation.RollbackAsync(root, state, host, options.GetValueOrDefault("--build-id"), cancellation.Token).ConfigureAwait(false)
        : await NativeInstallation.ApplyAsync(Required("--package"), Required("--sha256"), root, options.GetValueOrDefault("--nu"), state, host,
            command == "install", options.ContainsKey("--allow-downgrade"), cancellation.Token, Progress).ConfigureAwait(false);
    if (json) Console.WriteLine(JsonSerializer.Serialize(result, SetupJson.Default.NativeInstallationResult));
    else
    {
        Console.WriteLine($"{result.Action}: ModpackTools {result.Version}\nRoot: {result.Root}\nBuild: {result.BuildId}\nLauncher: {result.Launcher}\nNu module: {result.NuModule}");
        foreach (var diagnostic in result.Diagnostics) Console.Error.WriteLine(diagnostic.Code + ": " + diagnostic.Path + " — " + diagnostic.Message);
    }
    return 0;
}
catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
{
    var domain = error as DomainException ?? (error is OperationCanceledException
        ? new DomainException("Core.OperationCancelled", "Operation was cancelled; inspect the reported installation phases before retrying", "retry when ready", "OperationStopped") : null);
    if (json)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
        writer.WriteStartObject(); writer.WriteBoolean("ok", false); writer.WriteString("id", domain?.Id ?? "ModpackTools.Installation.Failed");
        writer.WriteString("message", error.Message); writer.WriteString("target", (domain?.Target as TextTarget)?.Value); writer.WriteString("details", domain?.Details);
        writer.WriteString("application_root", error.Data["ModpackTools.Installation.ApplicationRoot"] as string ?? applicationRoot);
        writer.WriteString("prepared_directory", error.Data["ModpackTools.Installation.PreparedDirectory"] as string);
        writer.WriteBoolean("retain_stage", error.Data["ModpackTools.RetainStage"] is true);
        writer.WriteBoolean("root_created", error.Data["ModpackTools.Installation.RootCreated"] is true);
        writer.WriteBoolean("version_published", error.Data["ModpackTools.Installation.VersionPublished"] is true);
        writer.WriteBoolean("bin_ready", error.Data["ModpackTools.Installation.BinReady"] is true); writer.WriteBoolean("control_switched", false); writer.WriteEndObject();
    }
    else Console.Error.WriteLine((domain?.Id ?? "ModpackTools.Installation.Failed") + ": " + error.Message
        + (domain?.Target is TextTarget target ? "\nTarget: " + target.Value : "") + (domain?.Details is { } detail ? "\n" + detail : ""));
    return 1;
}

static DomainException Invalid(string message) => new("Installation.InvalidArguments", message, "run native setup --help", "InvalidArgument");

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(NativeInstallationResult))]
[JsonSerializable(typeof(NativePackageBuild))]
[JsonSerializable(typeof(NativeRetirementResult))]
internal sealed partial class SetupJson : JsonSerializerContext;
