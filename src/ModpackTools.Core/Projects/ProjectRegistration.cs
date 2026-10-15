using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ModpackTools.Core.Data;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Projects;

public sealed record ProjectRegistrationRequest(string Id, string Path, string? DisplayName = null,
    string? DisplayVersion = null, string? OutputName = null);
public sealed record ProjectRegistrationResult(ProjectContext Project, string TechnicalName, string PackVersion, ImmutableArray<string> CreatedFiles);

public static class ProjectRegistration
{
    public static async Task<TransactionResult<ProjectRegistrationResult>> ExecuteAsync(string catalogRoot,
        ProjectRegistrationRequest request, string stateDirectory, CancellationToken cancellation = default,
        Func<TransactionPreview<ProjectRegistrationResult>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Id) || request.Id.Length > 4096 ||
            !Regex.IsMatch(request.Id, "^[a-z][a-z0-9-]*$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking))
            throw Error("InvalidId", $"Project ID '{request.Id}' is invalid; allowed characters: lowercase letters, numbers, hyphens", "choose an ID such as my-pack", request.Id);
        if (string.IsNullOrWhiteSpace(request.Path))
            throw Error("InvalidPath", "Packwiz project path cannot be empty", "modpack project register <id> --path <existing-directory>", request.Path);
        cancellation.ThrowIfCancellationRequested();
        var root = Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(catalogRoot));
        var path = System.IO.Path.GetFullPath(request.Path);
        if (!Directory.Exists(path)) throw Error("PathNotFound", $"Packwiz project directory '{path}' does not exist", "modpack project register <id> --path <existing-directory>", path, "ObjectNotFound");
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path)!), root))
            throw Error("OutsideRoot", $"Packwiz project '{path}' is not a direct child of configured root '{root}'", "move the project directly below the configured root or configure its parent as the root", path);
        using var catalog = WindowsProjectIdentity.Open(root);
        using var physical = WindowsProjectIdentity.Open(path);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(physical.Value.CanonicalRoot), catalog.Value.CanonicalRoot))
            throw Error("OutsideRoot", $"Packwiz project '{path}' is not a direct child of configured root '{root}'", "move the project directly below the configured root or configure its parent as the root", path);
        await using var lease = await NamedProjectLease.AcquireAsync("catalog-" + catalog.Value.FileIdentity).ConfigureAwait(false);
        AssertUnregistered(path, path);
        var manifest = ReadManifest(path, out var manifestText);
        var preserveReadme = Path.Exists(Path.Combine(path, "README.md"));
        var preserveIgnore = Path.Exists(Path.Combine(path, ".gitignore"));
        var index = manifest.String("file", "index");
        if (string.IsNullOrWhiteSpace(index)) index = "index.toml";
        var indexPath = Path.GetFullPath(Path.Combine(path, index));
        if (Path.IsPathRooted(index) || !TransactionFiles.IsBelow(indexPath, path))
            throw Error("InvalidIndexPath", $"Packwiz index path '{index}' escapes project root '{path}'", "repair the [index] file value in pack.toml", index, "InvalidData");
        if (!File.Exists(indexPath)) throw Error("IndexNotFound", $"Packwiz project '{path}' is incomplete because index '{index}' is missing", "restore the Packwiz index before registering it with ModpackTools", indexPath, "ObjectNotFound");
        var loader = new[] { "fabric", "quilt", "neoforge", "forge" }.FirstOrDefault(value => manifest.String(value, "versions") is not null);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(manifest.String("name"))) missing.Add("name");
        if (string.IsNullOrWhiteSpace(manifest.String("version"))) missing.Add("version");
        if (string.IsNullOrWhiteSpace(manifest.String("minecraft", "versions"))) missing.Add("versions.minecraft");
        if (string.IsNullOrWhiteSpace(loader)) missing.Add("versions.<loader>");
        if (missing.Count != 0) throw new DomainException("Project.InvalidManifest", $"Packwiz manifest '{Path.Combine(path, "pack.toml")}' is missing required data", "repair pack.toml before registering it with ModpackTools", "InvalidData", Path.Combine(path, "pack.toml"), details: string.Join(", ", missing));
        if (ProjectReader.Discover(root).Projects.Any(project => StringComparer.CurrentCultureIgnoreCase.Equals(project.Id, request.Id)))
            throw Error("AlreadyExists", $"Project ID '{request.Id}' is already registered", "choose a different project ID or run modpack project use <id>", request.Id, "ResourceExists");
        ValidateDisplay(request.DisplayName, "Name", "name"); ValidateDisplay(request.DisplayVersion, "Version", "version");
        if (request.OutputName is { } output && (string.IsNullOrWhiteSpace(output) || output.Length > 4096 ||
            output.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(output) != output || !output.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)))
            throw Error("InvalidOutputName", $"Output name '{output}' must be a valid .mrpack filename", "--output-name <filename.mrpack>", output);
        // This context is prospective: no descriptor is written outside the stage.
        var prospective = new ProjectContext(request.Id, path, manifest.String("name"), manifest.String("version"), "", manifest.String("minecraft", "versions"), loader,
            loader is null ? null : manifest.String(loader, "versions"), index, indexPath);
        return await ProjectTransaction.ExecuteAsync(prospective, stateDirectory, Prepare, cancellation: cancellation, prepareOutput: prepareOutput).ConfigureAwait(false);

        Task<ProjectRegistrationResult> Prepare(ProjectContext stage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); catalog.AssertCurrent(); physical.AssertCurrent();
            AssertUnregistered(stage.Root, path);
            if (TextFile.Read(Path.Combine(stage.Root, "pack.toml")) != manifestText || !File.Exists(Path.Combine(stage.Root, index)))
                throw Error("RegistrationChanged", "Packwiz registration inputs changed during preparation", "review the project and retry", path, "InvalidData");
            var descriptor = ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("SchemaVersion", new PsdNumber("1")).Add("Id", new PsdString(request.Id));
            if (request.DisplayName is { } name) descriptor = descriptor.Add("DisplayName", new PsdString(name));
            if (request.DisplayVersion is { } version) descriptor = descriptor.Add("DisplayVersion", new PsdString(version));
            if (request.OutputName is { } output) descriptor = descriptor.Add("OutputName", new PsdString(output));
            Directory.CreateDirectory(Path.Combine(stage.Root, ".modpack"));
            Write(".modpack/project.psd1", PsdDataWriter.Write(new(descriptor), 1024 * 1024));
            var empty = new PsdTable(ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));
            Write(".modpack/metadata.psd1", PsdDataWriter.Write(new(empty.Entries.Add("Categories", empty).Add("Mods", empty).Add("ResourcePacks", empty)), 1024 * 1024));
            var project = ProjectReader.Read(stage.Root); ProjectStructure.Assert(project);
            var files = ImmutableArray.CreateBuilder<string>();
            files.Add(Path.Combine(path, ".modpack", "project.psd1")); files.Add(Path.Combine(path, ".modpack", "metadata.psd1"));
            if (!preserveReadme && !Path.Exists(Path.Combine(stage.Root, "README.md")))
            {
                using var resource = typeof(ProjectRegistration).Assembly.GetManifestResourceStream("ModpackTools.Core.Projects.ProjectReadme.txt") ?? throw new InvalidOperationException("Missing project README resource.");
                using var reader = new StreamReader(resource, new UTF8Encoding(false, true));
                var description = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(project.Loader ?? "Unknown");
                if (!string.IsNullOrEmpty(project.LoaderVersion)) description += " " + project.LoaderVersion;
                Write("README.md", string.Format(CultureInfo.CurrentCulture, reader.ReadToEnd(), project.Name, project.Version, project.Id, project.Minecraft, description, manifest.String("version"), project.OutputName));
                files.Add(Path.Combine(path, "README.md"));
            }
            if (!preserveIgnore && !Path.Exists(Path.Combine(stage.Root, ".gitignore"))) { Write(".gitignore", "dist/\n"); files.Add(Path.Combine(path, ".gitignore")); }
            return Task.FromResult(new ProjectRegistrationResult(project with { Root = path, IndexPath = indexPath }, manifest.String("name")!, manifest.String("version")!, files.ToImmutable()));
            void Write(string relative, string text) => File.WriteAllText(TransactionFiles.Within(stage.Root, relative), text, new UTF8Encoding(false, true));
        }
    }

    private static void ValidateDisplay(string? value, string field, string option)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 4096))
            throw Error("InvalidDisplay" + field, $"Display {option} cannot be empty", "--display-" + option + " <" + option + ">", value);
    }
    private static TomlDocument ReadManifest(string root, out string text)
    {
        var path = Path.Combine(root, "pack.toml");
        if (!File.Exists(path)) throw Error("ManifestNotFound", $"Directory '{root}' is not a Packwiz project because 'pack.toml' is missing", "run this command from an existing Packwiz project", path, "ObjectNotFound");
        try { text = TextFile.Read(path); return new(text); }
        catch (FormatException error) { throw new DomainException("Project.InvalidManifest", $"Packwiz manifest '{path}' is invalid TOML", "repair pack.toml", "InvalidData", path, details: error.Message); }
    }
    private static void AssertUnregistered(string root, string original)
    {
        var directory = Path.Combine(root, ".modpack"); var target = Path.Combine(original, ".modpack");
        if (File.Exists(directory)) throw Error("InitializationConflict", $"Path '{target}' exists but is not a directory", "move the conflicting path, then retry", target, "ResourceExists");
        if (File.Exists(Path.Combine(directory, "project.psd1"))) throw Error("AlreadyInitialized", $"Packwiz project '{original}' is already registered with ModpackTools", "modpack project list", Path.Combine(target, "project.psd1"), "ResourceExists");
        if (Directory.Exists(directory))
        {
            var entries = Directory.EnumerateFileSystemEntries(directory).Take(33).Select(Path.GetFileName).ToArray();
            if (entries.Length != 0)
            {
                var details = string.Join(", ", entries.Take(32).Order(StringComparer.CurrentCultureIgnoreCase));
                if (entries.Length > 32) details += ", ... (additional entries omitted)";
                throw new DomainException("Project.InitializationConflict", $"Directory '{target}' contains files that ModpackTools will not overwrite", "review or move the existing .modpack contents, then retry", "ResourceExists", target, details: details);
            }
        }
    }
    private static DomainException Error(string id, string message, string hint, string? target, string category = "InvalidArgument") => new("Project." + id, message, hint, category, target);
}
