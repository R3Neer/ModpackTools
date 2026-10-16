using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ModpackTools.Core.Source;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Data;

public enum ConfigurationSetting { Root, Packwiz }
public sealed record ConfigurationWriteRequest(ConfigurationSetting Setting, string Value);
public sealed record ConfigurationWritePreview(string Value, string Path, string? BeforeHash, string AfterHash);
public sealed record ConfigurationWriteResult(string Value, string Path, string Hash);

public sealed record ConfigurationObservation(ProjectPhysicalIdentity Directory, string? Hash);

public static class ConfigurationStore
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    public static ConfigurationObservation Observe(string configurationDirectory)
    {
        var directory = ProjectTransaction.CanonicalFutureDirectory(configurationDirectory);
        using var identity = WindowsProjectIdentity.Open(directory);
        var captured = Capture(Path.Combine(directory, "config.psd1"));
        identity.AssertCurrent();
        return new(identity.Value, captured.Hash);
    }

    public static async Task<ConfigurationWriteResult> SetAsync(string configurationDirectory, ConfigurationWriteRequest request,
        CancellationToken cancellation = default, Func<ConfigurationWritePreview, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Setting) || request.Value is null || request.Value.Length > 32767)
            throw new DomainException("Configuration.InvalidRequest", "Configuration write requires a supported setting and bounded value", "choose root or packwiz and a valid path");
        cancellation.ThrowIfCancellationRequested();
        var directory = ProjectTransaction.CanonicalFutureDirectory(configurationDirectory);
        // Use the canonical namespace even before directory creation, so creation
        // does not change the mutex name between concurrent initial writers.
        var path = System.IO.Path.Combine(directory, "config.psd1");
        await using var lease = await Acquire(directory, path).ConfigureAwait(false);
        WindowsProjectIdentity? identity = null; string? temporary = null; var owned = false; Exception? failure = null;
        try
        {
            if (Directory.Exists(directory)) identity = WindowsProjectIdentity.Open(directory);
            var captured = Capture(path); var entries = captured.Data.Entries;
            string value;
            if (request.Setting == ConfigurationSetting.Root)
            {
                value = System.IO.Path.GetFullPath(request.Value);
                if (!Directory.Exists(value)) throw new DomainException("Configuration.InvalidRoot", $"Modpack root '{value}' does not exist or is not a directory", "modpack config set root <existing-directory>", "ObjectNotFound", value);
                entries = entries.SetItem("Root", new PsdString(value));
            }
            else if (request.Value.Equals("auto", StringComparison.OrdinalIgnoreCase)) { entries = entries.Remove("PackwizPath"); value = "auto"; }
            else
            {
                value = System.IO.Path.GetFullPath(request.Value);
                if (!File.Exists(value)) throw new DomainException("Configuration.InvalidPackwizPath", $"Packwiz executable '{value}' does not exist", "modpack config set packwiz <existing-executable>", "ObjectNotFound", value);
                entries = entries.SetItem("PackwizPath", new PsdString(value));
            }
            var text = PsdDataWriter.Write(new(entries), 4 * 1024 * 1024); var encoding = new UTF8Encoding(false, true);
            if (encoding.GetByteCount(text) > MaximumBytes) throw new DomainException("Configuration.OutputLimit", "Configuration exceeds its output quota", "reduce configuration data", "LimitsExceeded", path);
            var bytes = encoding.GetBytes(text); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (prepareOutput is not null) await prepareOutput(new(value, path, captured.Hash, hash), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            WindowsProjectIdentity.GuardAncestors(directory);
            Directory.CreateDirectory(directory); identity ??= WindowsProjectIdentity.Open(directory); identity.AssertCurrent();
            WindowsProjectIdentity.GuardAncestors(path);
            temporary = System.IO.Path.Combine(directory, ".config-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            { owned = true; output.Write(bytes); output.Flush(flushToDisk: true); }
            cancellation.ThrowIfCancellationRequested(); identity.AssertCurrent();
            if (TransactionFiles.Hash(path, MaximumBytes, cancellation) != captured.Hash)
                throw new DomainException("Configuration.Changed", "Configuration changed during preparation; nothing was replaced", "review the external edit and retry", "ResourceBusy", path);
            // Existing-file replacement retains Windows destination metadata;
            // initial publication must never overwrite a competing first writer.
            if (captured.Hash is null) File.Move(temporary, path, overwrite: false);
            else File.Replace(temporary, path, destinationBackupFileName: null);
            owned = false;
            // Publication is terminal: no late cancellation/readback can turn it
            // into a reported unapplied write. Bytes were owned/validated earlier.
            return new(value, path, hash);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var failed = new DomainException("Configuration.WriteFailed", "Configuration could not be written safely", "inspect the exact configuration path and retry", "WriteError", path, details: error.Message);
            failure = failed; throw failed;
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            identity?.Dispose();
            if (owned)
            {
                try { File.Delete(temporary!); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    if (failure is not null) failure.Data["Configuration.CleanupFailure"] = temporary + ": " + error.Message;
                    else throw new DomainException("Configuration.CleanupFailed", "Configuration temporary could not be removed", "inspect the exact temporary path before retrying", "WriteError", temporary, details: error.Message);
                }
            }
        }
    }

    private static (PsdTable Data, string? Hash) Capture(string path)
    {
        WindowsProjectIdentity.GuardAncestors(path);
        if (!File.Exists(path)) return (new(ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.CurrentCultureIgnoreCase)), null);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumBytes) throw new FormatException("Data file exceeds the 8 MiB limit.");
            var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            return (PsdReader.Parse(reader.ReadToEnd()), Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException)
        { throw new DomainException("Configuration.InvalidFile", $"Configuration file '{path}' is not a valid PSD1 file", $"repair or remove '{path}', then run modpack config set root <directory>", "InvalidData", path, details: error.Message); }
    }

    private static async Task<NamedProjectLease> Acquire(string directory, string path)
    {
        try { return await NamedProjectLease.AcquireAsync("configuration-" + TextHash.Compute(directory.ToUpperInvariant())).ConfigureAwait(false); }
        catch (DomainException error) when (error.Id == "ModpackTools.Transaction.Busy")
        { throw new DomainException("Configuration.Busy", "Another operation owns this configuration write", "retry after the other configuration write finishes", "ResourceBusy", path); }
    }
}
