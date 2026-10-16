using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Distribution;
using ModpackTools.Core.Transactions;

internal static class NativePackageInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-native-package <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 }); var input = document.RootElement;
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                if (input.TryGetProperty("CreateFixture", out var fixture) && fixture.GetBoolean())
                {
                    var path = input.GetProperty("ArchivePath").GetString()!;
                    var executable = File.ReadAllBytes(input.GetProperty("Executable").GetString()!);
                    var files = NativePackageManifest.PayloadPaths.ToDictionary(name => name,
                        name => name.EndsWith(".exe", StringComparison.Ordinal) ? executable : Encoding.UTF8.GetBytes("Native package format fixture: " + name + "\n"), StringComparer.Ordinal);
                    if (input.TryGetProperty("LauncherExecutable", out var launcherExecutable)) files["modpack.exe"] = File.ReadAllBytes(launcherExecutable.GetString()!);
                    if (input.TryGetProperty("PayloadDirectory", out var payloadDirectory))
                        foreach (var name in NativePackageManifest.PayloadPaths.Where(name => !name.EndsWith(".exe", StringComparison.Ordinal)))
                        {
                            var sourcePath = Path.Combine(payloadDirectory.GetString()!, name);
                            if (File.Exists(sourcePath)) files[name] = File.ReadAllBytes(sourcePath);
                        }
                    if (input.TryGetProperty("FixtureMarker", out var marker))
                    {
                        var text = marker.GetString()!;
                        if (text.Length is < 1 or > 64 || text.Any(character => !char.IsAsciiLetterOrDigit(character))) throw new ArgumentException("FixtureMarker must be bounded ASCII alphanumeric text.");
                        files["Nushell/modpack.nu"] = Encoding.UTF8.GetBytes("export def package-marker [] { '" + text + "' }\n");
                    }
                    var manifest = NativePackageManifest.Create("4.0.2", files.Select(pair => new NativePackageFile(pair.Key, pair.Value.Length, Convert.ToHexString(SHA256.HashData(pair.Value)))));
                    var manifestBytes = manifest.ToBytes();
                    if (input.TryGetProperty("CorruptPayload", out var corrupt)) files[corrupt.GetString()!] = Encoding.UTF8.GetBytes("corrupted payload\n");
                    if (input.TryGetProperty("ManifestText", out var replacement)) manifestBytes = Encoding.UTF8.GetBytes(replacement.GetString()!);
                    using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var archive = new ZipArchive(destination, ZipArchiveMode.Create))
                    {
                        foreach (var pair in files)
                        { using var entry = archive.CreateEntry(pair.Key, CompressionLevel.Fastest).Open(); entry.Write(pair.Value); }
                        using var manifestEntry = archive.CreateEntry("package.json").Open(); manifestEntry.Write(manifestBytes);
                    }
                    writer.WriteStartObject(); writer.WriteString("ArchivePath", path);
                    writer.WriteString("ArchiveSha256", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                    writer.WriteString("BuildId", manifest.BuildId); writer.WriteEndObject();
                }
                else
                {
                    using var cancellation = new CancellationTokenSource();
                    var events = new List<NativePackageProgress>(); var changed = false;
                    var prepared = NativePackageArchive.Prepare(input.GetProperty("ArchivePath").GetString()!, input.GetProperty("ArchiveSha256").GetString()!,
                        input.GetProperty("StagingParent").GetString()!, cancellation: cancellation.Token, progress: value =>
                        {
                            if (events.Count < 4096) events.Add(value);
                            if (!changed && value.Phase == "extracted")
                            {
                                if (input.TryGetProperty("ChangeCompletedFileAndCancel", out var change) && change.GetBoolean())
                                { File.AppendAllText(value.Path, "foreign edit"); changed = true; cancellation.Cancel(); }
                                else if (input.TryGetProperty("AddStreamAndCancel", out var add) && add.GetBoolean())
                                { File.WriteAllText(value.Path + ":foreign", "foreign stream"); changed = true; cancellation.Cancel(); }
                                else if (input.TryGetProperty("AddDirectoryStreamAndCancel", out var addDirectory) && addDirectory.GetBoolean())
                                { File.WriteAllText(Path.GetDirectoryName(value.Path) + ":foreign", "foreign directory stream"); changed = true; cancellation.Cancel(); }
                            }
                        });
                    NativePackageArchive.Verify(prepared);
                    if (input.TryGetProperty("InitializeInstallation", out var initialize))
                        _ = NativeInstallationRoot.Create(initialize.GetString()!, input.GetProperty("NuExecutable").GetString()!);
                    NativePublishedVersion? publication = null;
                    if (input.TryGetProperty("PublishParent", out var publishParent))
                    {
                        var parentPath = publishParent.GetString()!; Directory.CreateDirectory(parentPath);
                        var parentIdentity = FileTreeTransaction.InspectIdentity(parentPath); var publicationChanged = false;
                        publication = NativeVersionPublication.PublishAsync(prepared, parentIdentity, cancellation: cancellation.Token, progress: value =>
                        {
                            if (publicationChanged || value.Phase != "publish-copied") return;
                            if (input.TryGetProperty("ChangePublishedFileAndCancel", out var change) && change.GetBoolean())
                            { File.AppendAllText(value.Path, "foreign publication edit"); publicationChanged = true; cancellation.Cancel(); }
                            else if (input.TryGetProperty("OccupyVersionTarget", out var occupy) && occupy.GetBoolean())
                            {
                                var target = Path.Combine(parentPath, prepared.Manifest.BuildId); Directory.CreateDirectory(target);
                                File.WriteAllText(Path.Combine(target, "foreign.txt"), "foreign occupied version"); publicationChanged = true;
                            }
                        }).GetAwaiter().GetResult();
                        prepared = publication.Package;
                    }
                    NativeControlSwitch? switched = null;
                    if (input.TryGetProperty("ControlRoot", out var controlRoot))
                    {
                        var controlPath = controlRoot.GetString()!; Directory.CreateDirectory(controlPath);
                        var expected = input.TryGetProperty("ExpectedGeneration", out var generation) ? generation.GetString() : null;
                        switched = NativeInstallationControl.SwitchAsync(FileTreeTransaction.InspectIdentity(controlPath), input.GetProperty("StateDirectory").GetString()!,
                            prepared, expected, cancellation: cancellation.Token, observer: value =>
                            {
                                if (value.Phase == TransactionPhase.FileCommitted && value.CompletedFiles == 1
                                    && input.TryGetProperty("FailAfterControlFile", out var fail) && fail.GetBoolean())
                                    throw new IOException("Controlled first-file publication failure");
                                return ValueTask.CompletedTask;
                            }).GetAwaiter().GetResult();
                    }
                    if (input.TryGetProperty("VerifyAdmissionPin", out var verifyPin))
                    {
                        NativeLaunchPin pin;
                        var root = verifyPin.GetString()!;
                        var admission = NativeInstallationRoot.AcquireAsync(root).GetAwaiter().GetResult();
                        try { pin = admission.PinCurrent(); }
                        finally { admission.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                        try { using var unexpected = admission.PinCurrent(); throw new InvalidOperationException("Disposed admission could still create a version pin"); }
                        catch (ObjectDisposedException) { }
                        var path = Path.Combine(root, "pins", pin.Version.BuildId + ".lease");
                        using (pin)
                        {
                            try { using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); throw new InvalidOperationException("Active admission pin did not prevent retirement access"); }
                            catch (IOException) { }
                        }
                        using var released = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    writer.WriteStartObject(); writer.WriteString("Directory", prepared.Directory); writer.WriteString("BuildId", prepared.Manifest.BuildId);
                    if (publication is not null) writer.WriteBoolean("Published", publication.Published);
                    if (switched is not null)
                    {
                        writer.WriteBoolean("ControlApplied", switched.Applied); writer.WriteString("Generation", switched.Current.Generation);
                        writer.WriteString("PreviousGeneration", switched.Previous?.Generation); writer.WriteString("PreviousBuildId", switched.Current.PreviousBuildId);
                        writer.WriteNumber("RetiredCount", switched.Current.Retired.Length); writer.WriteNumber("ControlDiagnostics", switched.Diagnostics.Length);
                    }
                    writer.WriteString("ArchiveSha256", prepared.ArchiveSha256); writer.WriteString("FileIdentity", prepared.Identity.FileIdentity);
                    writer.WriteStartArray("Files");
                    foreach (var file in prepared.Files)
                    { writer.WriteStartObject(); writer.WriteString("Path", file.Path); writer.WriteNumber("Length", file.Length); writer.WriteString("Sha256", file.Sha256); writer.WriteEndObject(); }
                    writer.WriteEndArray(); writer.WriteNumber("ProgressEvents", events.Count); writer.WriteEndObject();
                }
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error)
        {
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteString("Id", error.Id); writer.WriteString("Message", error.Message);
                writer.WriteString("Category", error.Category); writer.WriteString("Target", (error.Target as TextTarget)?.Value); writer.WriteString("Details", error.Details);
                writer.WriteBoolean("RetainStage", error.Data["ModpackTools.RetainStage"] is true); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 1;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check native package inspection input", null); return 2; }
    }
}
