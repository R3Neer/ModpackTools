using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Build;

public sealed record ProjectIndexLimits(int MaximumRows = 100000, int MaximumEntries = 1000000, int MaximumDirectories = 100000,
    long MaximumHashedBytes = 1L << 40, int MaximumDocumentBytes = 8 * 1024 * 1024,
    int MaximumIssues = 256, int MaximumDetailCharacters = 1024 * 1024, long MaximumPathCharacters = 8 * 1024 * 1024);
public sealed record ProjectIndexReport(int Rows, ImmutableArray<string> Issues)
{
    public bool Valid => Issues.IsEmpty;
}

public static class ProjectIndex
{
    // A read observation, not a source lease. Build callers verify the owned
    // transaction stage; read-only health callers must bind their own source view.
    public static ProjectIndexReport Read(ProjectContext project, ProjectIndexLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(project); limits ??= new();
        if (limits.MaximumRows <= 0 || limits.MaximumEntries <= 0 || limits.MaximumDirectories <= 0 || limits.MaximumHashedBytes <= 0
            || limits.MaximumDocumentBytes is <= 0 or > 8 * 1024 * 1024 || limits.MaximumIssues <= 0 || limits.MaximumDetailCharacters <= 0 || limits.MaximumPathCharacters <= 0)
            throw new DomainException("Build.InvalidIndexPolicy", "Index verification quotas must be positive and bounded", "use explicit bounded index quotas");
        var issues = ImmutableArray.CreateBuilder<string>(); var rows = 0; long bytes = 0; var detailCharacters = 0;
        var digests = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(project.Root);
            var manifest = Document(TransactionFiles.Within(project.Root, "pack.toml"));
            if (manifest.Value.Get("index") is not TomlFields packIndex) throw new FormatException("pack.toml has no index table.");
            var relative = Text(packIndex.Get("file")); if (relative.Length == 0) relative = "index.toml";
            var indexPath = TransactionFiles.Within(project.Root, relative); var index = Document(indexPath);
            var algorithm = Algorithm(Text(packIndex.Get("hash-format")));
            if (algorithm.Name is null) Issue("Unsupported index hash algorithm");
            else if (!Equal(Text(packIndex.Get("hash")), Convert.ToHexString(Hash(index.Bytes, algorithm)))) Issue("pack.toml index hash is stale");
            if (index.Value.Get("files") is not TomlSequence files) throw new FormatException("Index files must be an array.");
            if (files.Items.Length > limits.MaximumRows) throw Limit("MaximumRows");
            rows = files.Items.Length;
            if (rows == 0) Issue("Index contains no files");
            var indexed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); var indexRoot = Path.GetDirectoryName(indexPath)!;
            var defaultAlgorithm = Text(index.Value.Get("hash-format"));
            foreach (var entry in files.Items)
            {
                cancellation.ThrowIfCancellationRequested();
                if (entry is not TomlFields row) throw new FormatException("Each index entry must be a table.");
                var name = Text(row.Get("file")); var path = TransactionFiles.Within(indexRoot, name);
                if (row.Get("metafile") is not (TomlMissing or TomlBoolean)) throw new FormatException("Index metafile must be a boolean.");
                var metadata = row.Get("metafile") is TomlBoolean { Value: true };
                if (!indexed.TryAdd(path, metadata)) Issue("Index repeats file " + name);
                var hashAlgorithm = Algorithm(row.Contains("hash-format") ? Text(row.Get("hash-format")) : defaultAlgorithm);
                if (!File.Exists(path)) Issue("Index references missing file " + name);
                else if (hashAlgorithm.Name is null) Issue("Unsupported hash algorithm for " + name);
                else if (!Equal(Text(row.Get("hash")), Digest(path, hashAlgorithm))) Issue("Index hash is stale for " + name);
            }
            foreach (var path in Metadata().Order(StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested(); var name = Path.GetRelativePath(project.Root, path).Replace('\\', '/');
                if (!indexed.TryGetValue(path, out var metadata)) Issue("Index omits managed metadata " + name);
                else if (!metadata) Issue("Index metadata flag is missing for " + name);
            }
        }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Index verification was cancelled", "retry when ready", "OperationStopped"); }
        catch (ManifestLimitException error) { throw Limit(error.Property); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or DecoderFallbackException) { Issue(error.Message); }
        return new(rows, issues.ToImmutable());

        void Issue(string message)
        {
            if (issues.Count >= limits.MaximumIssues) throw Limit("MaximumIssues");
            if (message.Length > limits.MaximumDetailCharacters - detailCharacters) throw Limit("MaximumDetailCharacters");
            detailCharacters += message.Length; issues.Add(message);
        }
        void Charge(int count)
        {
            cancellation.ThrowIfCancellationRequested();
            if (count > limits.MaximumHashedBytes - bytes) throw Limit("MaximumHashedBytes"); bytes += count;
        }
        (TomlFields Value, byte[] Bytes) Document(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            WindowsProjectIdentity.AssertStreamBytes(file.SafeFileHandle, limits.MaximumDocumentBytes, path);
            using var copy = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = file.Read(buffer)) != 0)
            {
                Charge(count); if (count > limits.MaximumDocumentBytes - copy.Length) throw Limit("MaximumDocumentBytes"); copy.Write(buffer, 0, count);
            }
            var data = copy.ToArray(); copy.Position = 0;
            using var reader = new StreamReader(copy, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            return (TomlDeclarationReader.Read(reader.ReadToEnd(), new()), data);
        }
        string Digest(string path, HashAlgorithmName name)
        {
            if (!digests.TryGetValue(path, out var values)) digests.Add(path, values = new(StringComparer.Ordinal));
            if (values.TryGetValue(name.Name!, out var prior)) return prior;
            WindowsProjectIdentity.GuardAncestors(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            WindowsProjectIdentity.AssertStreamBytes(file.SafeFileHandle, limits.MaximumHashedBytes - bytes, path);
            using var hash = IncrementalHash.CreateHash(name); var buffer = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                int count; while ((count = file.Read(buffer, 0, 65536)) != 0) { Charge(count); hash.AppendData(buffer, 0, count); }
                cancellation.ThrowIfCancellationRequested(); var digest = Convert.ToHexString(hash.GetHashAndReset()); values.Add(name.Name!, digest); return digest;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
        IEnumerable<string> Metadata()
        {
            var pending = new Stack<string>(); var directories = 0; var entries = 0; long paths = 0;
            foreach (var folder in new[] { "mods", "resourcepacks", "shaderpacks" })
            {
                var path = TransactionFiles.Within(project.Root, folder);
                if (Directory.Exists(path)) AddDirectory(path);
                else if (File.Exists(path)) throw new IOException("Content directory is occupied by a file: " + path);
            }
            while (pending.TryPop(out var directory))
            {
                cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(directory);
                foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++entries > limits.MaximumEntries) throw Limit("MaximumEntries");
                    if (path.Length > limits.MaximumPathCharacters - paths) throw Limit("MaximumPathCharacters"); paths += path.Length;
                    WindowsProjectIdentity.GuardAncestors(path); var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.Directory) != 0) AddDirectory(path);
                    else if (path.EndsWith(".pw.toml", StringComparison.OrdinalIgnoreCase)) yield return path;
                }
            }
            void AddDirectory(string path)
            {
                if (++directories > limits.MaximumDirectories) throw Limit("MaximumDirectories"); pending.Push(path);
            }
        }
    }
    private static bool Equal(string expected, string actual) => StringComparer.OrdinalIgnoreCase.Equals(expected, actual);
    private static string Text(TomlDeclarationValue value) => value is TomlText text ? text.Value : "";
    private static HashAlgorithmName Algorithm(string value) => value.ToLowerInvariant() switch
    { "sha256" => HashAlgorithmName.SHA256, "sha512" => HashAlgorithmName.SHA512, "sha1" => HashAlgorithmName.SHA1, "md5" => HashAlgorithmName.MD5, _ => default };
    private static byte[] Hash(byte[] bytes, HashAlgorithmName algorithm)
    { using var hash = IncrementalHash.CreateHash(algorithm); hash.AppendData(bytes); return hash.GetHashAndReset(); }
    private static DomainException Limit(string target) => new("Build.IndexVerificationLimit", "Index verification exceeds its resource quota", "use explicit bounded index quotas", "LimitsExceeded", target);
}
