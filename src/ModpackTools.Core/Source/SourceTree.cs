using System.Buffers;
using System.Collections.Frozen;
using System.Security.Cryptography;

namespace ModpackTools.Core.Source;

public sealed record SourceTreeReadLimits(int MaximumFiles = 100_000, int MaximumDirectories = 100_000,
    long MaximumBytes = 1L << 40, long MaximumPathCharacters = 8 * 1024 * 1024);

public sealed class SourceTreeSnapshot
{
    internal SourceTreeSnapshot(string root, Dictionary<string, string> files)
    {
        Root = root;
        Files = files.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
    public string Root { get; }
    public FrozenDictionary<string, string> Files { get; }
}

public static class SourceTree
{
    private static readonly EnumerationOptions Enumeration = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    public static SourceTreeSnapshot Read(string root, SourceTreeReadLimits? limits = null, CancellationToken cancellation = default)
        => ReadCore(root, limits, cancellation, false);
    public static SourceTreeSnapshot ReadForHealth(string root, SourceTreeReadLimits? limits = null, CancellationToken cancellation = default)
        => ReadCore(root, limits, cancellation, true);
    private static SourceTreeSnapshot ReadCore(string root, SourceTreeReadLimits? limits, CancellationToken cancellation, bool health)
    {
        limits ??= new();
        var invalid = limits.MaximumFiles <= 0 ? nameof(limits.MaximumFiles)
            : limits.MaximumDirectories <= 0 ? nameof(limits.MaximumDirectories)
            : limits.MaximumBytes <= 0 ? nameof(limits.MaximumBytes)
            : limits.MaximumPathCharacters <= 0 ? nameof(limits.MaximumPathCharacters) : null;
        if (invalid is not null)
            throw new DomainException("Source.InvalidTreeReadPolicy", "Source-tree reader limits must be positive",
                "use positive file, directory, byte and path-character quotas", target: invalid);
        var absolute = Path.GetFullPath(root);
        var current = absolute;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var budget = new TreeBudget(absolute, limits);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(absolute);
            FileSystemInfo first = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(absolute) : new FileInfo(absolute);
            CheckLink(first, root);
            if (first is FileInfo file)
            {
                if (!IsGit(file.Name)) ReadFile(file, ".", files, budget, cancellation);
                return new(absolute, files);
            }
            var pending = new Stack<DirectoryInfo>();
            budget.Directory(first.FullName, ".");
            pending.Push((DirectoryInfo)first);
            while (pending.TryPop(out var directory))
            {
                cancellation.ThrowIfCancellationRequested();
                using var entries = directory.EnumerateFileSystemInfos("*", Enumeration).GetEnumerator();
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    current = directory.FullName;
                    if (!entries.MoveNext()) break;
                    var entry = entries.Current;
                    if (IsGit(entry.Name)) continue;
                    if (health && entry is DirectoryInfo && string.Equals(entry.Name, "dist", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Path.TrimEndingDirectorySeparator(directory.FullName), Path.TrimEndingDirectorySeparator(absolute), StringComparison.OrdinalIgnoreCase)) continue;
                    current = entry.FullName;
                    CheckLink(entry);
                    var relative = Path.GetRelativePath(absolute, entry.FullName).Replace('\\', '/');
                    if (entry is DirectoryInfo child)
                    {
                        budget.Directory(child.FullName, relative);
                        pending.Push(child);
                    }
                    else ReadFile((FileInfo)entry, relative, files, budget, cancellation);
                }
            }
            return new(absolute, files);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DomainException("Source.TreeReadFailed", $"Source path '{current}' could not be read completely",
                "check source-path access permissions and retry with regular readable files",
                exception is FileNotFoundException or DirectoryNotFoundException ? "ObjectNotFound" : "ReadError", current, details: exception.Message);
        }
    }

    private static bool IsGit(string name) => string.Equals(name, ".git", StringComparison.InvariantCultureIgnoreCase);
    private static void CheckLink(FileSystemInfo entry, string? displayedPath = null)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 && !string.IsNullOrEmpty(entry.LinkTarget))
            throw new DomainException("Transaction.LinkedPath", $"Linked path '{displayedPath ?? entry.FullName}' cannot be transacted safely",
                "use a project with regular files and directories", "InvalidOperation");
    }
    internal static DomainException Collision(string path, string original) => new("Transaction.PathCollision",
        $"Source path '{path}' aliases another protected path", "rename colliding source paths before transacting the project",
        "InvalidOperation", path, details: "Existing spelling: " + original);

    private static void ReadFile(FileInfo file, string relative, Dictionary<string, string> files, TreeBudget budget, CancellationToken cancellation)
    {
        budget.File(file.FullName, relative);
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int count;
            while ((count = stream.Read(buffer, 0, 64 * 1024)) != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                budget.Bytes(count, file.FullName);
                hash.AppendData(buffer.AsSpan(0, count));
            }
            files.Add(relative, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private sealed class TreeBudget(string root, SourceTreeReadLimits limits)
    {
        private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        private int files;
        private int directories;
        private long bytes;
        private long characters;
        public void Directory(string full, string relative)
        {
            Path(full, relative);
            if (directories >= limits.MaximumDirectories) Limit(nameof(limits.MaximumDirectories), full);
            directories++;
        }
        public void File(string full, string relative)
        {
            Path(full, relative);
            if (files >= limits.MaximumFiles) Limit(nameof(limits.MaximumFiles), full);
            files++;
        }
        private void Path(string full, string relative)
        {
            if (paths.TryGetValue(relative, out var original)) throw Collision(relative, original);
            if (full.Length > limits.MaximumPathCharacters - characters) Limit(nameof(limits.MaximumPathCharacters), full);
            characters += full.Length;
            paths.Add(relative);
        }
        public void Bytes(int count, string path)
        {
            if (count > limits.MaximumBytes - bytes) Limit(nameof(limits.MaximumBytes), path);
            bytes += count;
        }
        private void Limit(string name, string context) => throw new DomainException("Source.TreeReadLimitExceeded",
            "Source tree exceeds its read limits", "reduce the protected source tree or use explicit positive source-tree quotas",
            "LimitsExceeded", root, details: name + " exceeded while reading '" + context + "'.");
    }
}
