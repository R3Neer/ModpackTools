using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Build;

public sealed record BuildRequest(bool NoRefresh = false, bool KeepOld = false, bool RawLog = false, bool Strict = false, bool DryRun = false);
public sealed record BuildLimits(ProjectHealthLimits? Health = null, ProjectIndexLimits? Index = null, MrpackReadLimits? Archive = null,
    TransactionLimits? Transaction = null, NativeToolLimits? Tool = null, int MaximumLogCharacters = 4 * 1024 * 1024);
public sealed record BuildResult(string Path, long Size, TimeSpan Duration, ImmutableArray<string> Log, GraphReport Health, RawInventorySnapshot Inventory, bool DryRun)
{
    public ImmutableArray<MrpackRecord> Snapshot { get; init; } = [];
}

public static partial class BuildOperation
{
    public static Task<TransactionResult<BuildResult>> ExecuteAsync(ProjectContext project, BuildRequest request, string configurationDirectory,
        string stateDirectory, DateTimeOffset now, Func<PackwizObservation> resolvePackwiz, string nativeHost, BuildLimits? limits = null,
        TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Func<TransactionPreview<BuildResult>, CancellationToken, ValueTask>? prepareOutput = null,
        Func<TransactionEvent, ValueTask>? observer = null, Uri? providerBaseUri = null,
        Func<HttpMessageHandler>? providerHandler = null, Func<HttpMessageHandler>? artifactHandler = null)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(resolvePackwiz); limits ??= new();
        if (limits.MaximumLogCharacters <= 0) throw new DomainException("Build.InvalidPolicy", "Build log quota must be positive", "use a bounded build log quota");
        if (string.IsNullOrEmpty(project.OutputName) || Path.GetFileName(project.OutputName) != project.OutputName || !project.OutputName.EndsWith(".mrpack", StringComparison.Ordinal))
            throw new DomainException("Build.InvalidOutputName", "OutputName must be an MRPack filename", "repair the project descriptor OutputName", "InvalidData");
        ProjectStructure.Assert(project); var config = MutationConfiguration.Resolve(project, configurationDirectory);
        PackwizObservation? tool = null;
        PackwizObservation Tool()
        {
            tool ??= resolvePackwiz();
            if (!tool.Available || tool.Path is null) throw new DomainException("Dependency.PackwizNotFound", "Required Packwiz executable is not available", "configure an existing native Packwiz executable", "ObjectNotFound", tool.Path);
            return tool;
        }
        return ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: request.DryRun, expected: expected,
            limits: limits.Transaction, cancellation: cancellation, prepareOutput: prepareOutput, observer: observer);

        async Task<BuildResult> Prepare(ProjectContext stage, CancellationToken token)
        {
            var health = await ProjectHealth.ReadAsync(stage, config, now, check: true, limits: limits.Health, cancellation: token,
                providerHandler: providerHandler?.Invoke(), artifactHandler: artifactHandler?.Invoke(), providerBaseUri: providerBaseUri).ConfigureAwait(false);
            GraphPolicy.Assert(health.Report, strict: request.Strict, build: true);
            ProjectIndexReport? index = null;
            if (request.NoRefresh) { index = ProjectIndex.Read(stage, limits.Index, token); AssertIndex(index, false); }
            var started = Stopwatch.StartNew(); var logs = ImmutableArray.CreateBuilder<string>(); var logCharacters = 0;
            if (!request.NoRefresh) AddLog((await PackwizRefresh.RefreshAsync(stage, Tool(), nativeHost, limits.Tool, token).ConfigureAwait(false)).Output);
            index ??= ProjectIndex.Read(stage, limits.Index, token); AssertIndex(index, true);
            var expectation = BuildArtifactExpectation.Read(stage, index, health, token);
            var metadataPath = TransactionFiles.Within(stage.Root, ".modpack/metadata.psd1");
            var descriptorPath = TransactionFiles.Within(stage.Root, ".modpack/project.psd1");
            var editorialHash = TransactionFiles.Hash(metadataPath, 8 * 1024 * 1024, token); var descriptorHash = TransactionFiles.Hash(descriptorPath, 8 * 1024 * 1024, token);
            var dist = TransactionFiles.Within(stage.Root, "dist"); Directory.CreateDirectory(dist);
            var name = project.OutputName;
            if (request.KeepOld && File.Exists(TransactionFiles.Within(dist, name)))
            {
                name = Path.GetFileNameWithoutExtension(name) + "-" + now.LocalDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".mrpack";
                if (File.Exists(TransactionFiles.Within(dist, name))) throw new DomainException("Build.ArtifactCollision", "The timestamped build already exists", "retry with a later timestamp to preserve the existing build", "ResourceExists", name);
            }
            var final = TransactionFiles.Within(dist, name); var temporary = TransactionFiles.Within(dist, ".modpacktools-" + Guid.NewGuid().ToString("N") + ".mrpack");
            var retain = false; Exception? failure = null;
            try
            {
                AddLog(await NativeTool.RunAsync(Tool().Path!, ["modrinth", "export", "--output", temporary], stage.Root, nativeHost, limits.Tool, token).ConfigureAwait(false));
                if (!File.Exists(temporary)) throw new DomainException("Build.ArtifactMissing", "Packwiz did not generate the expected build artifact", "modpack build --raw-log", "InvalidResult", TransactionFiles.Within(project.Root, "dist/" + name));
                var after = ProjectIndex.Read(stage, limits.Index, token); AssertIndex(after, true);
                var payloads = index.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
                if (after.Files.Length != payloads.Count || after.Files.Any(file => !payloads.TryGetValue(file.RelativePath, out var before) || before.Metadata != file.Metadata || before.Sha256 != file.Sha256)
                    || ProjectReader.Read(stage.Root) != stage || TransactionFiles.Hash(metadataPath, 8 * 1024 * 1024, token) != editorialHash || TransactionFiles.Hash(descriptorPath, 8 * 1024 * 1024, token) != descriptorHash)
                    throw new DomainException("Build.SourceChanged", "Export changed frozen project inputs", "inspect the Packwiz export before retrying", "InvalidResult");
                var archive = MrpackSnapshot.ReadDocument(temporary, limits.Archive, token);
                await BuildArtifactValidation.VerifyAsync(expectation, archive, config, limits.Health?.Artifacts, stage.Loader ?? "", token, artifactHandler?.Invoke()).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); File.Move(temporary, final, overwrite: true); started.Stop();
                var inventory = Rebase(health.Inventory, project);
                return new(TransactionFiles.Within(project.Root, "dist/" + name), new FileInfo(final).Length, started.Elapsed,
                    [.. logs.Where(line => request.RawLog || !ManifestAddedLine().IsMatch(line))], health.Report, inventory, request.DryRun) { Snapshot = archive.Records };
            }
            catch (Exception error) { failure = error; retain = error.Data["ModpackTools.RetainStage"] is true; throw; }
            finally
            {
                if (!retain && File.Exists(temporary))
                    try { WindowsProjectIdentity.GuardAncestors(temporary); File.Delete(temporary); }
                    catch (Exception error) when (failure is not null && error is IOException or UnauthorizedAccessException) { failure.Data["BuildTemporaryCleanup"] = error.Message; }
            }
            void AddLog(NativeToolResult result)
            {
                foreach (var text in new[] { result.Stdout, result.Stderr })
                    foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                    {
                        if (line.Length == 0) continue;
                        if (line.Length > limits.MaximumLogCharacters - logCharacters) throw new DomainException("Build.LogLimitExceeded", "Build output exceeds its log quota", "use an explicit bounded log quota", "LimitsExceeded", "MaximumLogCharacters");
                        logCharacters += line.Length; logs.Add(line);
                    }
            }
        }
    }
    private static void AssertIndex(ProjectIndexReport report, bool prepared)
    {
        if (!report.Valid) throw new DomainException(prepared ? "Build.InvalidIndex" : "Build.StaleIndex",
            prepared ? "Export preparation left an inconsistent index" : "The index cannot be exported without refresh",
            prepared ? "inspect the Packwiz refresh result" : "omit --no-refresh", prepared ? "InvalidResult" : "InvalidData", details: string.Join("; ", report.Issues));
    }
    private static RawInventorySnapshot Rebase(RawInventorySnapshot snapshot, ProjectContext project)
    {
        InventoryItem Item(InventoryItem item) => item.MetadataPath is { } path ? item with { MetadataPath = TransactionFiles.Within(project.Root, Path.GetRelativePath(snapshot.Project.Root, path)) } : item;
        var resources = snapshot.Resources.Select(Item).ToImmutableArray();
        return snapshot with { Project = project, Mods = [.. snapshot.Mods.Select(Item)], Resources = resources, Shaders = [.. snapshot.Shaders.Select(Item)],
            ResourceView = ResourcePackActivation.Compose(resources, [.. snapshot.ResourceView.Active.Select(item => item.Id)], snapshot.Metadata) };
    }
    [GeneratedRegex(@"\sadded to manifest\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ManifestAddedLine();
}
