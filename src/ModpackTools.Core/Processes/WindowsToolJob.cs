using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ModpackTools.Core.Processes;

internal sealed partial class WindowsToolJob : IDisposable
{
    private readonly JobHandle handle;
    public unsafe WindowsToolJob()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw Failure("Native tool ownership requires Windows x64");
        handle = CreateJobObjectW(0, 0);
        if (handle.IsInvalid) { handle.Dispose(); throw Failure("Tool job could not be created"); }
        var information = new ExtendedLimits { Basic = new() { Flags = 0x2000 } };
        if (SetInformationJobObject(handle, 9, in information, (uint)sizeof(ExtendedLimits)) == 0)
        { handle.Dispose(); throw Failure("Tool job limits could not be set"); }
    }
    public void Assign(Process process)
    { if (AssignProcessToJobObject(handle, process.SafeHandle) == 0) throw Failure("Waiting tool helper could not be assigned to its job"); }
    public void Terminate()
    { if (TerminateJobObject(handle, 1) == 0) throw Failure("Owned tool job could not be terminated"); }
    public unsafe bool Empty
    {
        get
        {
            if (QueryInformationJobObject(handle, 1, out var information, (uint)sizeof(Accounting), out _) == 0)
                throw Failure("Owned tool job state could not be verified");
            return information.ActiveProcesses == 0;
        }
    }
    public void Dispose() => handle.Dispose();
    private static DomainException Failure(string message) => new("Build.ProcessOwnershipFailed", message,
        "inspect the executable and Windows process ownership before retrying", "ResourceUnavailable", details: new Win32Exception(Marshal.GetLastPInvokeError()).Message);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime; public uint Flags; public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit; public nuint Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    { public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime; public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses; }
    private sealed class JobHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    { protected override bool ReleaseHandle() => CloseHandle(handle) != 0; }
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial JobHandle CreateJobObjectW(nint attributes, nint name);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int SetInformationJobObject(JobHandle job, int kind, in ExtendedLimits information, uint bytes);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int AssignProcessToJobObject(JobHandle job, SafeProcessHandle process);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int QueryInformationJobObject(JobHandle job, int kind, out Accounting information, uint bytes, out uint returned);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int TerminateJobObject(JobHandle job, uint code);
    [LibraryImport("kernel32.dll")] private static partial int CloseHandle(nint handle);
}
