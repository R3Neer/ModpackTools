using System.Diagnostics;
using ModpackTools.Core;
using ModpackTools.Core.Distribution;

if (args is ["--launcher-protocol"]) { Console.WriteLine("1"); return 0; }
try
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Launcher executable path is unavailable");
    var bin = Path.GetDirectoryName(executable)!;
    if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(bin), "bin")) throw new InvalidOperationException("Run the stable launcher from the managed application's bin directory");
    NativeLaunchPin pin;
    await using (var admission = await NativeInstallationRoot.AcquireAsync(Path.GetDirectoryName(bin)!).ConfigureAwait(false))
        pin = admission.PinCurrent();
    using (pin)
    {
        var options = args.TakeWhile(argument => argument != "--").ToArray();
        var machine = options.Contains("--no-human", StringComparer.Ordinal);
        var start = new ProcessStartInfo(machine ? Path.Combine(pin.Directory, "modpacktools.exe") : pin.NuExecutable) { UseShellExecute = false };
        if (!machine)
        { start.ArgumentList.Add("--no-config-file"); start.ArgumentList.Add(Path.Combine(pin.Directory, "cli.nu")); }
        else if (!options.Contains("--json", StringComparer.Ordinal)) start.ArgumentList.Add("--json");
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
