using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Distribution;

public sealed record NativePackageBuild(string Path, string Sha256, string BuildId, long Length);

public static class NativePackageBuilder
{
    public static NativePackageBuild Build(string core, string launcher, string setup, string assets, string output,
        CancellationToken cancellation = default, Action<NativePackageProgress>? progress = null)
    {
        assets = Path.GetFullPath(assets); output = Path.GetFullPath(output);
        WindowsProjectIdentity.GuardAncestors(output);
        using var assetIdentity = WindowsProjectIdentity.Open(assets);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in NativePackageManifest.PayloadPaths)
            sources[name] = Path.GetFullPath(name switch
            {
                "modpacktools.exe" => core,
                "modpack.exe" => launcher,
                "modpacktools-setup.exe" => setup,
                "licenses/Tomlyn.txt" => Path.Combine(assets, "Private", "lib", "LICENSE.Tomlyn.txt"),
                "licenses/Newtonsoft.Json.txt" => Path.Combine(assets, "Private", "lib", "Newtonsoft.Json.LICENSE.txt"),
                _ => Path.Combine(assets, name)
            });
        var files = ImmutableArray.CreateBuilder<NativePackageFile>(); long total = 0;
        foreach (var name in NativePackageManifest.PayloadPaths)
        {
            cancellation.ThrowIfCancellationRequested(); var path = sources[name]; WindowsProjectIdentity.GuardAncestors(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, 128L * 1024 * 1024, path, defaultStreamOnly: true);
            var maximum = name.EndsWith(".exe", StringComparison.Ordinal) ? 128L * 1024 * 1024
                : name.EndsWith(".nu", StringComparison.Ordinal) ? 2L * 1024 * 1024 : name.EndsWith(".json", StringComparison.Ordinal) ? 8L * 1024 * 1024 : 1024 * 1024;
            if (input.Length <= 0 || input.Length > maximum || input.Length > 512L * 1024 * 1024 - total)
                throw Error("BuildLimit", "Package build input exceeds its byte quota", path);
            var digest = NativePackageArchive.Copy(input, Stream.Null, input.Length, cancellation); total += input.Length;
            if (name.EndsWith(".exe", StringComparison.Ordinal)) NativePackageArchive.VerifyNativeExecutable(path);
            files.Add(new(name, input.Length, digest));
        }
        var manifest = NativePackageManifest.Create(ProductVersion.Current.Version, files); var canonical = manifest.ToBytes();
        string? stage = null; string? expectedHash = null;
        try
        {
            var parent = Path.GetDirectoryName(output)!;
            using var parentIdentity = WindowsProjectIdentity.Open(parent);
            var candidate = Path.Combine(parent, ".modpacktools-build-" + Guid.NewGuid().ToString("N") + ".zip");
            using (var destination = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stage = candidate;
                using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
                {
                    long completed = 0; var count = 0;
                    foreach (var file in manifest.Files)
                    {
                        cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(sources[file.Path]);
                        using var input = new FileStream(sources[file.Path], FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
                        WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, file.Length, file.Path, defaultStreamOnly: true);
                        var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal); entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using var target = entry.Open();
                        if (NativePackageArchive.Copy(input, target, file.Length, cancellation) != file.Sha256)
                            throw Error("BuildSourceChanged", "Package build input changed while it was copied", sources[file.Path]);
                        completed += file.Length; count++;
                        progress?.Invoke(new("pack", sources[file.Path], completed, total, count, manifest.Files.Length));
                    }
                    var metadata = archive.CreateEntry("package.json", CompressionLevel.Optimal); metadata.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var manifestOutput = metadata.Open(); manifestOutput.Write(canonical);
                }
                destination.Flush(flushToDisk: true);
            }
            expectedHash = TransactionFiles.Hash(stage, 256L * 1024 * 1024, cancellation)!;
            var result = new NativePackageBuild(output, expectedHash, manifest.BuildId, new FileInfo(stage).Length);
            assetIdentity.AssertCurrent(); parentIdentity.AssertCurrent(); cancellation.ThrowIfCancellationRequested();
            File.Move(stage, output, overwrite: false); stage = null; return result;
        }
        catch (Exception error)
        {
            if (stage is not null)
            {
                // A partial ZIP is not guessed safe; complete known bytes can be
                // removed after a target collision, otherwise preserve evidence.
                try
                {
                    WindowsProjectIdentity.GuardAncestors(stage);
                    using (var input = new FileStream(stage, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        WindowsProjectIdentity.AssertStreamBytes(input.SafeFileHandle, 256L * 1024 * 1024, stage, defaultStreamOnly: true);
                        if (expectedHash is null || NativePackageArchive.Copy(input, Stream.Null, input.Length, CancellationToken.None) != expectedHash)
                            throw new IOException("The package build stage has no complete verified cleanup identity");
                    }
                    File.Delete(stage);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DomainException)
                {
                    var retained = Error("BuildStageRetained", "Package build failed; inspect the retained archive", stage, error.Message + "; " + cleanup.Message);
                    retained.Data["ModpackTools.RetainStage"] = true; throw retained;
                }
            }
            throw;
        }
    }

    private static DomainException Error(string id, string message, string path, string? details = null) => NativePackageManifest.Error(id, message, path, details);
}
