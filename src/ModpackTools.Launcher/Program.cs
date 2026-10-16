using System.Diagnostics;
using ModpackTools.Core;
using ModpackTools.Core.Distribution;

if (args is ["--launcher-protocol"]) { Console.WriteLine("1"); return 0; }
try
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Launcher executable path is unavailable");
    var directory = Path.GetDirectoryName(executable)!;
    var stable = StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(directory), "bin");
    var parent = Path.GetDirectoryName(directory)!;
    if (!stable && !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(parent), "versions")) throw new InvalidOperationException("Run the launcher from its managed application directory");
    var root = stable ? parent : Path.GetDirectoryName(parent)!;
    NativeLaunchPin pin;
    await using (var admission = await NativeInstallationRoot.AcquireAsync(root).ConfigureAwait(false))
        pin = stable ? admission.PinCurrent() : admission.PinVersion(Path.GetFileName(directory));
    using (pin)
    {
        var options = args.TakeWhile(argument => argument != "--").ToArray();
        var machine = options.Contains("--no-human", StringComparer.Ordinal);
        var start = new ProcessStartInfo(stable ? Path.Combine(pin.Directory, "modpack.exe")
            : machine ? Path.Combine(pin.Directory, "modpacktools.exe") : pin.NuExecutable)
        { UseShellExecute = false };
        if (!stable && !machine)
        { start.ArgumentList.Add("--no-config-file"); start.ArgumentList.Add(Path.Combine(pin.Directory, "cli.nu")); }
        else if (!stable && !options.Contains("--json", StringComparer.Ordinal)) start.ArgumentList.Add("--json");
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("Selected version could not be started");
        Console.CancelKeyPress += (_, eventArgs) => eventArgs.Cancel = true;
        await child.WaitForExitAsync().ConfigureAwait(false); return child.ExitCode;
    }
}
catch (DomainException error)
{ Console.Error.WriteLine(error.Id + ": " + error.Message + (error.Target is TextTarget target ? "\nTarget: " + target.Value : "")); return 1; }
catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
{ Console.Error.WriteLine("ModpackTools.Installation.LaunchFailed: " + error.Message); return 1; }
