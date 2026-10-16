using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Processes;

// Process.Start supplies a null lpApplicationName on Windows, imposing MAX_PATH
// on the executable parsed from the command line. Keep extended names at this
// boundary and supply the application explicitly; never shorten or relocate cwd.
public sealed partial class WindowsProcess : IDisposable
{
    private Process? process;
    private StreamWriter? input;
    private StreamReader? output, error;
    private WindowsProjectIdentity? workingDirectory;
    public ProcessStartInfo StartInfo { get; init; } = new();
    public SafeProcessHandle SafeHandle => Required.SafeHandle;
    public int ExitCode => Required.ExitCode;
    public bool HasExited => Required.HasExited;
    public StreamWriter StandardInput => input ?? throw new InvalidOperationException("Input is not redirected");
    public StreamReader StandardOutput => output ?? throw new InvalidOperationException("Output is not redirected");
    public StreamReader StandardError => error ?? throw new InvalidOperationException("Error is not redirected");
    private Process Required => process ?? throw new InvalidOperationException("Process has not started");
    public Task WaitForExitAsync(CancellationToken cancellation = default) => Required.WaitForExitAsync(cancellation);
    public void Kill(bool entireProcessTree) => Required.Kill(entireProcessTree);

    public static WindowsProcess StartNew(ProcessStartInfo start)
    {
        var child = new WindowsProcess { StartInfo = start };
        try { child.Start(); return child; }
        catch { child.Dispose(); throw; }
    }

    public unsafe bool Start()
    {
        if (process is not null) throw new InvalidOperationException("Process already started");
        var start = StartInfo;
        if (!OperatingSystem.IsWindows() || start.UseShellExecute || !Path.IsPathFullyQualified(start.FileName))
            throw new InvalidOperationException("Native process startup requires Windows and an absolute executable without a shell");
        var command = string.Join(' ', new[] { start.FileName }.Concat(start.ArgumentList).Select(Quote));
        if (command.Length >= 32767) throw new Win32Exception(206, "Native command line exceeds the Windows Unicode limit");
        var environment = string.Join('\0', start.Environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Where(pair => pair.Value is not null).Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
        var originalDirectory = Path.GetFullPath(string.IsNullOrEmpty(start.WorkingDirectory) ? Environment.CurrentDirectory : start.WorkingDirectory);
        var cwd = originalDirectory;
        var pipes = new List<SafeFileHandle>();
        var startup = new Startup { Size = (uint)sizeof(Startup) };
        nint attributes = 0;
        var information = default(ProcessInformation);
        try
        {
            if (cwd.Length >= 248)
            {
                // CreateProcess's cwd has a separate Win32 limit. An existing
                // short name changes no files or global policy; verify identity.
                workingDirectory = WindowsProjectIdentity.Open(cwd);
                var buffer = new char[32768]; uint length;
                fixed (char* pointer = buffer) length = GetShortPathNameW(WindowsNativePath.From(cwd), pointer, (uint)buffer.Length);
                if (length != 0 && length < buffer.Length)
                {
                    cwd = new string(buffer, 0, checked((int)length));
                    if (cwd.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) cwd = @"\\" + cwd[8..];
                    else if (cwd.StartsWith(@"\\?\", StringComparison.Ordinal)) cwd = cwd[4..];
                }
                if (cwd.Length >= 248) throw new DomainException("Build.WorkingDirectoryTooLong", "Windows cannot start in this directory and no usable short name exists; use a shorter native state directory",
                    "use a shorter native state directory on this volume", "InvalidArgument", originalDirectory);
                using var alias = WindowsProjectIdentity.Open(cwd);
                if (alias.Value.FileIdentity != workingDirectory.Value.FileIdentity) throw new IOException("Short working directory identity differs from the requested directory");
            }
            startup.Flags = 0x100; // STARTF_USESTDHANDLES, including inherited Nu/CI pipes
            startup.Input = ChildHandle(start.RedirectStandardInput, true, -10, out var parentInput);
            startup.Output = ChildHandle(start.RedirectStandardOutput, false, -11, out var parentOutput);
            startup.Error = ChildHandle(start.RedirectStandardError, false, -12, out var parentError);
            var inherited = new[] { startup.Input, startup.Output, startup.Error }.Where(value => value != 0 && value != -1).ToArray();
            if (inherited.Length != 0)
            {
                nuint size = 0;
                _ = InitializeProcThreadAttributeList(0, 1, 0, ref size);
                attributes = Marshal.AllocHGlobal(checked((nint)size));
                if (InitializeProcThreadAttributeList(attributes, 1, 0, ref size) == 0) throw LastError();
                startup.Attributes = attributes;
                fixed (nint* handles = inherited)
                {
                    if (UpdateProcThreadAttribute(attributes, 0, 0x20002, (nint)handles, (nuint)(inherited.Length * sizeof(nint)), 0, 0) == 0) throw LastError();
                    Spawn(true);
                }
            }
            else Spawn(false);
            if (parentInput is not null) { input = new(new FileStream(parentInput, FileAccess.Write), new UTF8Encoding(false)); pipes.Remove(parentInput); }
            if (parentOutput is not null) { output = new(new FileStream(parentOutput, FileAccess.Read), Encoding.UTF8); pipes.Remove(parentOutput); }
            if (parentError is not null) { error = new(new FileStream(parentError, FileAccess.Read), Encoding.UTF8); pipes.Remove(parentError); }
            // Open the managed process handle while the child is suspended, avoiding
            // the GetProcessById race with programs that exit immediately.
            process = Process.GetProcessById(checked((int)information.ProcessId));
            _ = process.SafeHandle;
            if (ResumeThread(information.Thread) == uint.MaxValue) throw LastError();
            return true;

            void Spawn(bool inherit)
            {
                if (!inherit) startup.Size = (uint)(sizeof(Startup) - sizeof(nint));
                fixed (char* commandPointer = (command + '\0').ToCharArray())
                fixed (char* environmentPointer = environment)
                    if (CreateProcessW(WindowsNativePath.From(start.FileName), commandPointer, 0, 0, inherit ? 1 : 0,
                        0x400 | 0x4 | (inherit ? 0x80000u : 0) | (start.CreateNoWindow ? 0x08000000u : 0),
                        environmentPointer, cwd, in startup, out information) == 0) throw LastError();
            }
        }
        catch (Exception failure)
        {
            var uncertain = false;
            if (information.Process != 0) { _ = TerminateProcess(information.Process, 1); uncertain = WaitForSingleObject(information.Process, 10000) != 0; }
            Dispose();
            if (uncertain)
            {
                var retained = new DomainException("Build.ProcessOwnershipUncertain", "Native child termination could not be confirmed",
                    "retain its working directory and inspect the child process before retrying", "OperationStopped", cwd,
                    details: $"PID: {information.ProcessId}; executable: {start.FileName}; original failure: {failure.Message}");
                retained.Data["ModpackTools.RetainStage"] = true; throw retained;
            }
            throw;
        }
        finally
        {
            foreach (var pipe in pipes) pipe.Dispose();
            if (startup.Attributes != 0) DeleteProcThreadAttributeList(startup.Attributes);
            if (attributes != 0) Marshal.FreeHGlobal(attributes);
            if (information.Thread != 0) _ = CloseHandle(information.Thread);
            if (information.Process != 0) _ = CloseHandle(information.Process);
        }

        nint ChildHandle(bool redirect, bool read, int standard, out SafeFileHandle? parent)
        {
            parent = null;
            if (!redirect)
            {
                var original = GetStdHandle(standard);
                if (original == 0 || original == -1) return original;
                if (DuplicateHandle(GetCurrentProcess(), original, GetCurrentProcess(), out var duplicate, 0, 1, 2) == 0) throw LastError();
                var safe = new SafeFileHandle(duplicate, true); pipes.Add(safe); return duplicate;
            }
            var security = new SecurityAttributes { Length = (uint)sizeof(SecurityAttributes), Inherit = 1 };
            if (CreatePipe(out var reader, out var writer, in security, 0) == 0) throw LastError();
            pipes.Add(reader); pipes.Add(writer);
            parent = read ? writer : reader;
            if (SetHandleInformation(parent, 1, 0) == 0) throw LastError();
            return (read ? reader : writer).DangerousGetHandle();
        }
    }

    private static string Quote(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("Process arguments cannot contain NUL");
        var text = new StringBuilder("\""); var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes); slashes = 0;
            text.Append(character);
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static Win32Exception LastError() => new(Marshal.GetLastPInvokeError());
    public void Dispose() { input?.Dispose(); output?.Dispose(); error?.Dispose(); process?.Dispose(); workingDirectory?.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public uint Length; public nint Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct Startup
    {
        public uint Size; public nint Reserved, Desktop, Title;
        public uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public ushort Show, ReservedBytes; public nint ReservedData, Input, Output, Error, Attributes;
    }
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int CreateProcessW(string application, char* command, nint processAttributes, nint threadAttributes, int inherit, uint flags, char* environment, string directory, in Startup startup, out ProcessInformation information);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int CreatePipe(out SafeFileHandle read, out SafeFileHandle write, in SecurityAttributes security, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returned);
    [LibraryImport("kernel32.dll")] private static partial void DeleteProcThreadAttributeList(nint list);
    [LibraryImport("kernel32.dll")] private static partial nint GetCurrentProcess();
    [LibraryImport("kernel32.dll")] private static partial nint GetStdHandle(int kind);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int DuplicateHandle(nint sourceProcess, nint source, nint targetProcess, out nint target, uint access, int inherit, uint options);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial uint ResumeThread(nint thread);
    [LibraryImport("kernel32.dll")] private static partial int TerminateProcess(nint process, uint code);
    [LibraryImport("kernel32.dll")] private static partial uint WaitForSingleObject(nint handle, uint timeout);
    [LibraryImport("kernel32.dll")] private static partial int CloseHandle(nint handle);
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] private static unsafe partial uint GetShortPathNameW(string path, char* output, uint size);
}
