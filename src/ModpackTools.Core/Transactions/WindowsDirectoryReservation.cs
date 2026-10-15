using System.Runtime.InteropServices;

namespace ModpackTools.Core.Transactions;

internal static partial class WindowsDirectoryReservation
{
    public static bool TryCreate(string path)
    { WindowsProjectIdentity.GuardAncestors(path); return CreateDirectory(WindowsNativePath.From(path), 0) != 0; }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateDirectory(string path, nint security);
}
