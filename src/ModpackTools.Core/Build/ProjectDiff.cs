using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Build;

public sealed record ProjectDiffResult(string BaselinePath, DateTime BaselineUtc, MrpackComparison Comparison);

public static class ProjectDiff
{
    public static Task<TransactionResult<ProjectDiffResult>> CompareAsync(ProjectContext project, string configurationDirectory,
        string stateDirectory, Func<PackwizObservation> resolvePackwiz, string nativeHost, BuildLimits? limits = null,
        CancellationToken cancellation = default, Func<TransactionPreview<ProjectDiffResult>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(resolvePackwiz); limits ??= new();
        ProjectStructure.Assert(project); var config = MutationConfiguration.Resolve(project, configurationDirectory);
        return ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: true, limits: limits.Transaction, cancellation: cancellation, prepareOutput: prepareOutput);
        async Task<ProjectDiffResult> Prepare(ProjectContext stage, CancellationToken token)
        {
            var baseline = Latest(stage, project, token); var snapshot = MrpackSnapshot.Read(baseline.FullName, limits.Archive, token);
            var inventory = InventoryReader.ReadForHealth(stage, limits.Health?.Inventory);
            var tool = resolvePackwiz();
            if (!tool.Available || tool.Path is null) throw new DomainException("Dependency.PackwizNotFound", "Required Packwiz executable is not available", "configure an existing native Packwiz executable", "ObjectNotFound", tool.Path);
            _ = await PackwizRefresh.RefreshAsync(stage, tool, nativeHost, limits.Tool, token).ConfigureAwait(false);
            var index = ProjectIndex.Read(stage, limits.Index, token); AssertIndex(index);
            var expected = BuildArtifactExpectation.Read(stage, index, inventory, token);
            var metadata = TransactionFiles.Within(stage.Root, ".modpack/metadata.psd1"); var descriptor = TransactionFiles.Within(stage.Root, ".modpack/project.psd1");
            var metadataHash = TransactionFiles.Hash(metadata, 8 * 1024 * 1024, token); var descriptorHash = TransactionFiles.Hash(descriptor, 8 * 1024 * 1024, token);
            var temporary = TransactionFiles.Within(stage.Root, "dist/.modpacktools-compare-" + Guid.NewGuid().ToString("N") + ".mrpack");
            _ = await NativeTool.RunAsync(tool.Path, ["modrinth", "export", "--output", temporary], stage.Root, nativeHost, limits.Tool, token).ConfigureAwait(false);
            if (!File.Exists(temporary)) throw new DomainException("Diff.TemporaryArtifactMissing", "Packwiz did not generate the temporary comparison artifact", "modpack build --raw-log", "InvalidResult");
            var after = ProjectIndex.Read(stage, limits.Index, token); AssertIndex(after);
            var payloads = index.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
            if (after.Files.Length != payloads.Count || after.Files.Any(file => !payloads.TryGetValue(file.RelativePath, out var before) || before.Metadata != file.Metadata || before.Sha256 != file.Sha256)
                || ProjectReader.Read(stage.Root) != stage || TransactionFiles.Hash(metadata, 8 * 1024 * 1024, token) != metadataHash || TransactionFiles.Hash(descriptor, 8 * 1024 * 1024, token) != descriptorHash)
                throw new DomainException("Build.SourceChanged", "Export changed frozen project inputs", "inspect the Packwiz export before retrying", "InvalidResult");
            var current = MrpackSnapshot.ReadDocument(temporary, limits.Archive, token);
            await BuildArtifactValidation.VerifyAsync(expected, current, config, limits.Health?.Artifacts, stage.Loader ?? "", token).ConfigureAwait(false);
            var comparison = MrpackSnapshot.Compare(snapshot, current.Records, cancellation: token);
            token.ThrowIfCancellationRequested(); File.Delete(temporary);
            return new(TransactionFiles.Within(project.Root, "dist/" + baseline.Name), baseline.LastWriteTimeUtc, comparison);
        }
    }
    private static FileInfo Latest(ProjectContext stage, ProjectContext original, CancellationToken cancellation)
    {
        var dist = TransactionFiles.Within(stage.Root, "dist"); FileInfo? latest = null; var entries = 0;
        if (Directory.Exists(dist))
            foreach (var item in new DirectoryInfo(dist).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, IgnoreInaccessible = false }))
            {
                cancellation.ThrowIfCancellationRequested(); if (++entries > 100000) throw new DomainException("Diff.ArtifactLimit", "Build directory exceeds its entry quota", "reduce the build history", "LimitsExceeded");
                WindowsProjectIdentity.GuardAncestors(item.FullName);
                if (item is not FileInfo file || !file.Name.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || file.Name.StartsWith(".modpacktools-", StringComparison.Ordinal)) continue;
                if (latest is null || file.LastWriteTimeUtc > latest.LastWriteTimeUtc || file.LastWriteTimeUtc == latest.LastWriteTimeUtc && StringComparer.Ordinal.Compare(file.Name, latest.Name) < 0) latest = file;
            }
        return latest ?? throw new DomainException("Diff.BaselineNotFound", $"No previous build exists in '{Path.Combine(original.Root, "dist")}'", "modpack build", "ObjectNotFound", Path.Combine(original.Root, "dist"));
    }
    private static void AssertIndex(ProjectIndexReport report)
    {
        if (!report.Valid) throw new DomainException("Build.InvalidIndex", "Export preparation left an inconsistent index", "inspect the Packwiz refresh result", "InvalidResult", details: string.Join("; ", report.Issues));
    }
}
