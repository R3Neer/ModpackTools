using System.ComponentModel;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ModpackTools.Core.Transactions;

public sealed record ProjectPhysicalIdentity(string CanonicalRoot, string FileIdentity);

internal sealed partial class WindowsProjectIdentity : IDisposable
{
    private readonly SafeFileHandle handle;
    public ProjectPhysicalIdentity Value { get; }
    private WindowsProjectIdentity(SafeFileHandle handle, ProjectPhysicalIdentity value) { this.handle = handle; Value = value; }

    public static ProjectPhysicalIdentity Inspect(string root) { using var opened = Open(root); return opened.Value; }

    public static unsafe WindowsProjectIdentity Open(string root)
    {
        if (!OperatingSystem.IsWindows()) throw Error("UnsupportedPlatform", "Project transactions require Windows file identity support");
        var absolute = Path.GetFullPath(root); GuardAncestors(absolute);
        // Keep the root open without delete sharing throughout the transaction.
        var handle = CreateFileW(absolute, 0x80, 3, 0, 3, 0x02000000 | 0x00200000, 0);
        if (handle.IsInvalid) { var error = Marshal.GetLastPInvokeError(); handle.Dispose(); throw Failure(absolute, error); }
        try
        {
            if (ReadAttributes(handle, 9, out var attributes, 8) == 0) throw Failure(absolute, Marshal.GetLastPInvokeError());
            if ((attributes.Attributes & 0x400) != 0 || (attributes.Attributes & 0x10) == 0) throw Error("LinkedPath", "Project root must be a regular directory", absolute);
            if (ReadIdentity(handle, 18, out var identity, 24) == 0) throw Failure(absolute, Marshal.GetLastPInvokeError());
            Span<char> buffer = stackalloc char[32768]; uint length;
            fixed (char* output = buffer) length = GetFinalPathNameByHandleW(handle, (nint)output, (uint)buffer.Length, 0);
            if (length == 0 || length >= buffer.Length) throw Failure(absolute, Marshal.GetLastPInvokeError());
            var canonical = new string(buffer[..checked((int)length)]);
            if (canonical.StartsWith("\\\\?\\UNC\\", StringComparison.Ordinal)) canonical = "\\\\" + canonical[8..];
            else if (canonical.StartsWith("\\\\?\\", StringComparison.Ordinal)) canonical = canonical[4..];
            return new(handle, new(Path.TrimEndingDirectorySeparator(canonical), $"{identity.Volume:x16}{identity.Low:x16}{identity.High:x16}"));
        }
        catch { handle.Dispose(); throw; }
    }

    public void AssertCurrent()
    {
        using var current = Open(Value.CanonicalRoot);
        if (current.Value != Value) throw Error("ConcurrentChange", "Project directory identity changed during the operation", Value.CanonicalRoot);
    }

    internal static unsafe void AssertStreamBytes(SafeFileHandle file, long maximum, string path)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            int success;
            fixed (byte* output = buffer) success = ReadStreamInformation(file, 7, (nint)output, 65536);
            if (success == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == 38) return; // No streams returned for this handle.
                throw Error("StreamMetadataReadFailed", "File data streams could not be bounded before copying", path, new Win32Exception(error).Message);
            }
            var offset = 0; long bytes = 0;
            while (true)
            {
                if (offset > 65536 - 24) throw Error("StreamMetadataReadFailed", "File stream metadata is invalid", path);
                var remaining = buffer.AsSpan(offset, 65536 - offset);
                var next = BinaryPrimitives.ReadUInt32LittleEndian(remaining);
                var nameBytes = BinaryPrimitives.ReadUInt32LittleEndian(remaining[4..]);
                var size = BinaryPrimitives.ReadInt64LittleEndian(remaining[8..]);
                if (nameBytes > remaining.Length - 24 || size < 0 || next != 0 && (next < 24 + nameBytes || next > remaining.Length))
                    throw Error("StreamMetadataReadFailed", "File stream metadata is invalid", path);
                if (size > maximum - bytes) throw new DomainException("Transaction.ResourceLimit", "File data streams exceed the copy byte quota", "use an explicit bounded byte quota", "LimitsExceeded", path);
                bytes += size; if (next == 0) return; offset += checked((int)next);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal static void GuardAncestors(string path)
    {
        for (FileSystemInfo? entry = new FileInfo(Path.GetFullPath(path)); entry is not null;
            entry = entry is DirectoryInfo directory ? directory.Parent : ((FileInfo)entry).Directory)
        {
            entry.Refresh();
            if (!string.IsNullOrEmpty(entry.LinkTarget) || entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw Error("LinkedPath", "Transaction paths must not traverse links or reparse points", entry.FullName);
        }
    }
    internal static DomainException Error(string id, string message, string? target = null, string? details = null) =>
        new("Transaction." + id, message, "inspect the project transaction state before retrying",
            id == "Busy" ? "ResourceBusy" : id == "IoFailed" ? "WriteError" : id is "InvalidJournal" or "InvalidBackup" or "UnsafePath" or "InvalidExpectation" ? "InvalidData" : "InvalidOperation", target, details: details);
    private static DomainException Failure(string path, int code) => Error("IdentityReadFailed", "Project directory identity could not be verified", path, new Win32Exception(code).Message);
    public void Dispose() => handle.Dispose();

    [StructLayout(LayoutKind.Sequential)] private struct Identity { public ulong Volume; public ulong Low; public ulong High; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeAttributes { public uint Attributes; public uint Tag; }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static partial int ReadIdentity(SafeFileHandle handle, int kind, out Identity identity, uint bytes);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static partial int ReadAttributes(SafeFileHandle handle, int kind, out NativeAttributes attributes, uint bytes);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static partial int ReadStreamInformation(SafeFileHandle handle, int kind, nint buffer, uint bytes);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandleW(SafeFileHandle handle, nint buffer, uint characters, uint flags);
}
