using System.Buffers;
using System.Security.Cryptography;

namespace ModpackTools.Core.Transactions;

internal static class TransactionFiles
{
    public static string Within(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains(':')
            || relative.Replace('\\', '/').Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || StringComparer.InvariantCultureIgnoreCase.Equals(part, ".git")))
            throw WindowsProjectIdentity.Error("UnsafePath", "Transaction relative path is unsafe", relative);
        var absoluteRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(absoluteRoot, relative));
        if (!path.StartsWith(absoluteRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw WindowsProjectIdentity.Error("UnsafePath", "Transaction path escapes its owned root", relative);
        WindowsProjectIdentity.GuardAncestors(path); return path;
    }

    public static string? Hash(string path, long maximumBytes, CancellationToken cancellation = default)
    {
        WindowsProjectIdentity.GuardAncestors(path);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        if ((attributes & FileAttributes.Directory) != 0) throw WindowsProjectIdentity.Error("UnsupportedTopology", "A transaction file path is occupied by a directory", path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(65536); long bytes = 0;
        try
        {
            int read;
            while ((read = file.Read(buffer, 0, 65536)) != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (read > maximumBytes - bytes) throw Limit(path);
                bytes += read; hash.AppendData(buffer.AsSpan(0, read));
            }
            cancellation.ThrowIfCancellationRequested(); return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public static void CopyVerified(string source, string destination, string expectedHash, long maximumBytes, CancellationToken cancellation, string? publicationTemporary = null)
    {
        WindowsProjectIdentity.GuardAncestors(source); WindowsProjectIdentity.GuardAncestors(destination);
        var parent = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(parent); WindowsProjectIdentity.GuardAncestors(parent);
        var temporary = publicationTemporary ?? Path.Combine(parent, ".modpacktools-" + Guid.NewGuid().ToString("N") + ".tmp");
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(temporary), parent)) throw WindowsProjectIdentity.Error("UnsafePath", "Publication temporary is not adjacent to its destination", temporary);
        WindowsProjectIdentity.GuardAncestors(temporary);
        var owned = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
            {
                cancellation.ThrowIfCancellationRequested();
                if (input.Length > maximumBytes) throw Limit(source);
                WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, maximumBytes, source);
                // Reserve with CreateNew before invoking the OS copy. File.Copy
                // preserves Windows data streams/attributes that a byte loop drops.
                using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) owned = true;
                File.Copy(source, temporary, overwrite: true);
                if (!StringComparer.Ordinal.Equals(Hash(temporary, maximumBytes, cancellation), expectedHash))
                    throw WindowsProjectIdentity.Error("ConcurrentChange", "Copy source differs from its protected hash", source);
            }
            cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(destination);
            File.Move(temporary, destination, overwrite: true); owned = false;
        }
        finally
        {
            if (owned) DeleteOwnedFile(temporary);
        }
    }

    public static bool IsBelow(string path, string root) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static string PublicationRelative(string relative, string transactionId, int index, bool rollback)
    {
        var parent = Path.GetDirectoryName(relative.Replace('/', Path.DirectorySeparatorChar));
        var filename = ".modpacktools-" + transactionId + (rollback ? "-rollback-" : "-commit-") + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tmp";
        return string.IsNullOrEmpty(parent) ? filename : Path.Combine(parent, filename).Replace('\\', '/');
    }

    // Only called after validating the workspace owner record and containment.
    // Delete reparse entries themselves; never enumerate through them.
    public static void DeleteOwnedTree(string home, string workspace, int maximumEntries)
    {
        if (!IsBelow(workspace, home)) throw WindowsProjectIdentity.Error("UnsafePath", "Workspace is outside the transaction home", workspace);
        WindowsProjectIdentity.GuardAncestors(workspace);
        var stack = new Stack<(string Path, bool Visited)>(); stack.Push((workspace, false)); var entries = 0;
        while (stack.TryPop(out var current))
        {
            if (current.Visited) { Directory.Delete(current.Path); continue; }
            if (++entries > maximumEntries) throw Limit(current.Path);
            stack.Push((current.Path, true));
            foreach (var entry in new DirectoryInfo(current.Path).EnumerateFileSystemInfos())
            {
                if (++entries > maximumEntries) throw Limit(entry.FullName);
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                { if ((entry.Attributes & FileAttributes.Directory) != 0) Directory.Delete(entry.FullName); else File.Delete(entry.FullName); }
                else if (entry is DirectoryInfo) stack.Push((entry.FullName, false));
                else DeleteOwnedFile(entry.FullName);
            }
        }
    }
    internal static void DeleteOwnedFile(string path)
    {
        if (!File.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        File.Delete(path);
    }
    private static DomainException Limit(string path) => new("Transaction.ResourceLimit", "Transaction file IO exceeds its resource quota", "use explicit bounded transaction quotas", "LimitsExceeded", path);
}
