using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using ModpackTools.Core.Data;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Source;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Projects;

public sealed record ProjectCreationRequest(string Id, string Name, string Minecraft, string Loader,
    string? LoaderVersion = null, string? DirectoryName = null, string PackVersion = "0.1.0", string? DisplayVersion = null);
public sealed record ProjectCreationResult(ProjectContext Project, string TechnicalName, string PackVersion,
    ImmutableArray<TransactionDiagnostic> Diagnostics);

public static class ProjectCreation
{
    public static void ValidateRequest(ProjectCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProjectRegistration.ValidateRequest(new(request.Id, "."));
        if (request.Loader is null || !new[] { "fabric", "quilt", "forge", "neoforge" }.Contains(request.Loader, StringComparer.OrdinalIgnoreCase))
            throw Error("UnsupportedLoader", $"Loader '{request.Loader}' is not supported by project creation; allowed values: fabric, quilt, forge, neoforge", "--loader <fabric|quilt|forge|neoforge>", request.Loader);
        if (new[] { request.Name, request.Minecraft, request.PackVersion, request.DisplayVersion ?? request.Minecraft }.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 4096)
            || request.LoaderVersion?.Length > 4096 || request.DirectoryName?.Length > 32767)
            throw Error("InvalidCreationRequest", "Project creation requires bounded nonempty name and version values", "supply a project name, Minecraft version and pack version", request.Id);
    }

    public static async Task<ProjectCreationResult> ExecuteAsync(string catalogRoot, ProjectCreationRequest request,
        PackwizObservation tool, string nativeHost, NativeToolLimits? processLimits = null, CancellationToken cancellation = default,
        Func<ProjectCreationResult, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ValidateRequest(request); cancellation.ThrowIfCancellationRequested();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(catalogRoot));
        using var catalog = WindowsProjectIdentity.Open(root);
        await using var lease = await NamedProjectLease.AcquireAsync("catalog-" + catalog.Value.FileIdentity).ConfigureAwait(false);
        if (ProjectReader.Discover(root).Projects.Any(project => StringComparer.CurrentCultureIgnoreCase.Equals(project.Id, request.Id)))
            throw Error("AlreadyExists", $"Project ID '{request.Id}' is already registered", "choose a different project ID or run modpack project use <id>", request.Id, "ResourceExists");
        var directory = string.IsNullOrEmpty(request.DirectoryName) ? Sanitize(request.Name).Trim() + "-" + request.Minecraft : request.DirectoryName;
        var target = Path.GetFullPath(Path.Combine(root, directory));
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(target), root))
            throw Error("DestinationOutsideRoot", $"Project destination '{target}' is not a direct child of configured root '{root}'", "choose a directory directly below the configured root", target);
        if (Path.Exists(target)) throw Error("DestinationExists", $"Project destination '{target}' already exists and will not be overwritten", "choose a different --path or remove the existing directory deliberately", target, "ResourceExists");
        if (!tool.Available || tool.Path is null) throw new DomainException("Dependency.PackwizNotFound", "Required Packwiz executable is not available", "configure an existing native Packwiz executable", "ObjectNotFound", tool.Path);
        var workspace = Path.Combine(root, ".modpacktools-create-" + Guid.NewGuid().ToString("N"));
        if (Path.Exists(workspace)) throw Error("WorkspaceCollision", "Creation workspace already exists", "retry with a new owned workspace", workspace, "ResourceExists");
        WindowsProjectIdentity.GuardAncestors(workspace); Directory.CreateDirectory(workspace);
        var stage = Path.Combine(workspace, "stage"); var published = false; var retain = false; Exception? primary = null;
        var diagnostics = ImmutableArray.CreateBuilder<TransactionDiagnostic>();
        ProjectCreationResult? result = null;
        try
        {
            Directory.CreateDirectory(stage);
            var arguments = new List<string> { "init", "--yes", "--name", request.Name, "--mc-version", request.Minecraft, "--version", request.PackVersion, "--modloader", request.Loader.ToLowerInvariant() };
            if (!string.IsNullOrEmpty(request.LoaderVersion)) { arguments.Add("--" + request.Loader.ToLowerInvariant() + "-version"); arguments.Add(request.LoaderVersion); }
            else arguments.Add("--" + request.Loader.ToLowerInvariant() + "-latest");
            _ = await NativeTool.RunAsync(tool.Path, arguments, stage, nativeHost, processLimits, cancellation).ConfigureAwait(false);
            _ = SourceTree.Read(stage, cancellation: cancellation);
            foreach (var name in new[] { ".modpack", "mods", "config", "resourcepacks", "shaderpacks", "dist" }) Directory.CreateDirectory(Path.Combine(stage, name));
            var displayVersion = request.DisplayVersion ?? request.Minecraft;
            var outputName = Sanitize(request.Name).Trim() + "-" + displayVersion + ".mrpack";
            if (outputName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw Error("InvalidOutputName", $"Output name '{outputName}' must be a valid .mrpack filename", "choose a display version without filename separators", outputName);
            var empty = new PsdTable(ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));
            Write(".modpack/project.psd1", PsdDataWriter.Write(new(empty.Entries.Add("SchemaVersion", new PsdNumber("1")).Add("Id", new PsdString(request.Id))
                .Add("DisplayName", new PsdString(request.Name)).Add("DisplayVersion", new PsdString(displayVersion)).Add("OutputName", new PsdString(outputName))), 1024 * 1024));
            Write(".modpack/metadata.psd1", PsdDataWriter.Write(new(empty.Entries.Add("Categories", empty).Add("Mods", empty).Add("ResourcePacks", empty)), 1024 * 1024));
            var project = ProjectReader.Read(stage); ProjectStructure.Assert(project);
            if (project.Minecraft != request.Minecraft || !StringComparer.OrdinalIgnoreCase.Equals(project.Loader, request.Loader)
                || request.LoaderVersion is { Length: > 0 } version && project.LoaderVersion != version)
                throw Error("InvalidInitialization", "Packwiz initialization does not match the requested Minecraft/loader", "inspect the initialization output", stage, "InvalidResult");
            var manifest = new TomlDocument(TextFile.Read(Path.Combine(stage, "pack.toml")));
            if (manifest.String("name") != request.Name || manifest.String("version") != request.PackVersion)
                throw Error("InvalidInitialization", "Packwiz initialization does not match the requested name/version", "inspect the initialization output", stage, "InvalidResult");
            Write("README.md", ProjectRegistration.CreateReadme(project, request.PackVersion)); Write(".gitignore", "dist/\n");
            var prepared = SourceFingerprint.All(SourceTree.Read(stage, cancellation: cancellation));
            result = new(project with { Root = target, IndexPath = Path.Combine(target, project.IndexFile) }, request.Name, request.PackVersion, []);
            if (prepareOutput is not null) await prepareOutput(result, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); catalog.AssertCurrent(); WindowsProjectIdentity.GuardAncestors(target);
            if (SourceFingerprint.All(SourceTree.Read(stage, cancellation: cancellation)) != prepared)
                throw Error("CreationChanged", "Prepared project changed before publication", "review the initialization output and retry", stage, "InvalidData");
            Directory.Move(stage, target); published = true;
        }
        catch (Exception error)
        {
            primary = error; retain = error.Data["ModpackTools.RetainStage"] is true;
            if (retain)
            {
                var summary = error is DomainException domain ? domain.Id + ": " + domain.Message + Environment.NewLine + domain.Details : error.Message;
                var failure = new DomainException("Project.CreationStageRetained", "Project was not published; process ownership is uncertain and its stage was preserved", "inspect the exact retained workspace before retrying", "OperationStopped", workspace,
                    details: summary.Length > 16384 ? summary[..16384] + " (details truncated)" : summary);
                failure.Data["ModpackTools.RetainStage"] = true; throw failure;
            }
            throw;
        }
        finally
        {
            if (!retain)
            {
                try { catalog.AssertCurrent(); TransactionFiles.DeleteOwnedTree(root, workspace, 1000000); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or DomainException)
                {
                    if (published) diagnostics.Add(new("Project.CreationCleanupPending", workspace, error.Message));
                    else if (primary is not null) primary.Data["ModpackTools.CreationCleanupPending"] = workspace + ": " + error.Message;
                    else throw;
                }
            }
        }
        return result! with { Diagnostics = diagnostics.ToImmutable() };

        void Write(string relative, string text)
        {
            var path = TransactionFiles.Within(stage, relative); WindowsProjectIdentity.GuardAncestors(path);
            if (Directory.Exists(path)) throw Error("InitializationConflict", $"Packwiz created a conflicting registration path '{relative}'", "inspect the initialization output", path, "ResourceExists");
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var bytes = new UTF8Encoding(false, true).GetBytes(text); file.Write(bytes); file.Flush(true);
        }
    }
    private static string Sanitize(string value) => Regex.Replace(value, "[\\\\/:*?\"<>|]", "-", RegexOptions.NonBacktracking);
    private static DomainException Error(string id, string message, string hint, string? target, string category = "InvalidArgument") => new("Project." + id, message, hint, category, target);
}
