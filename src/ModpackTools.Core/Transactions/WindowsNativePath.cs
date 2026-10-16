namespace ModpackTools.Core.Transactions;

// Raw Win32 calls need extended paths even when managed filesystem APIs already
// support long names. Keep this representation at the native boundary only.
internal static class WindowsNativePath
{
    public static string From(string path)
    {
        var absolute = Path.GetFullPath(path);
        if (absolute.StartsWith(@"\\?\", StringComparison.Ordinal)) return absolute;
        return absolute.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + absolute[2..]
            : @"\\?\" + absolute;
    }
}
