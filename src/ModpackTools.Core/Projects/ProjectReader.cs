using System.Globalization;
using System.Text.RegularExpressions;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Projects;

public static partial class ProjectReader
{
    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Identity();

    [GeneratedRegex("[\\\\/:*?\"<>|]", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidOutputCharacters();

    public static ProjectContext Read(string projectDirectory)
    {
        var root = Path.GetFullPath(projectDirectory);
        var path = Path.Combine(root, ".modpack", "project.psd1");
        if (!File.Exists(path))
            throw new DomainException("Project.DescriptorNotFound", $"Project descriptor '{path}' does not exist", "restore '.modpack/project.psd1' or register a valid project", "ObjectNotFound", path);
        PsdTable descriptor;
        try { descriptor = PsdReader.Parse(TextFile.Read(path)); }
        catch (FormatException exception)
        {
            throw new DomainException("Project.InvalidDescriptor", $"Project descriptor '{path}' is not a valid PSD1 file", "repair the project descriptor", "InvalidData", path, details: exception.Message);
        }
        foreach (var required in new[] { "SchemaVersion", "Id" })
            if (!descriptor.Entries.ContainsKey(required))
                throw new DomainException("Project.MissingField", $"Required field '{required}' is missing from project descriptor '{path}'", "repair the project descriptor", "InvalidData", required);
        var schema = descriptor.Entries["SchemaVersion"];
        if (DataScalars.Integer(schema) != 1)
            throw new DomainException("Project.UnsupportedSchema", $"Schema version '{DataScalars.String(schema)}' in project descriptor '{path}' is not supported", "upgrade ModpackTools or migrate the project descriptor", "InvalidData", targetData: DataScalars.Target(schema));
        var id = DataScalars.String(descriptor.Entries["Id"]);
        if (!Identity().IsMatch(id))
            throw new DomainException("Project.InvalidId", $"Project ID '{id}' in '{path}' is invalid; allowed characters: lowercase letters, numbers, hyphens", "edit the Id field in .modpack/project.psd1", "InvalidData", targetData: DataScalars.Target(descriptor.Entries["Id"]));
        var manifestPath = Path.Combine(root, "pack.toml");
        if (!File.Exists(manifestPath))
            throw new DomainException("Project.ManifestNotFound", $"Packwiz manifest '{manifestPath}' does not exist", "restore pack.toml or recreate the project", "ObjectNotFound", manifestPath);
        TomlDocument manifest;
        try
        {
            var text = TextFile.Read(manifestPath);
            if (text.Length == 0) throw new FormatException("The manifest is empty.");
            manifest = new(text);
        }
        catch (FormatException exception)
        {
            throw new DomainException("Project.InvalidManifest", $"Packwiz manifest '{manifestPath}' is invalid TOML", "repair pack.toml", "InvalidData", manifestPath, details: exception.Message);
        }
        var name = descriptor.Entries.TryGetValue("DisplayName", out var displayName) ? DataScalars.String(displayName) : manifest.String("name");
        var version = descriptor.Entries.TryGetValue("DisplayVersion", out var displayVersion) ? DataScalars.String(displayVersion) : manifest.String("version");
        var outputBase = InvalidOutputCharacters().Replace(name ?? "", "-").Trim();
        if (!string.IsNullOrEmpty(version)) outputBase += "-" + version;
        var output = descriptor.Entries.TryGetValue("OutputName", out var outputName) ? DataScalars.String(outputName) : outputBase + ".mrpack";
        var index = manifest.String("file", "index");
        if (string.IsNullOrWhiteSpace(index)) index = "index.toml";
        var indexPath = Path.GetFullPath(Path.Combine(root, index));
        if (Path.IsPathRooted(index) || !indexPath.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Project.InvalidIndexPath", Path.IsPathRooted(index) ? $"Packwiz index path '{index}' must be relative to the project" : $"Packwiz index path '{index}' escapes project root '{Path.TrimEndingDirectorySeparator(root)}'", "repair the [index] file value in pack.toml", "InvalidData", index);
        var loader = new[] { "fabric", "quilt", "neoforge", "forge" }.FirstOrDefault(candidate => manifest.String(candidate, "versions") is not null);
        var javaDeclared = descriptor.Entries.TryGetValue("JavaVersion", out var javaVersion);
        return new(id, root, name, version, output, manifest.String("minecraft", "versions"), loader,
            loader is null ? null : manifest.String(loader, "versions"), index, indexPath)
        { JavaDeclared = javaDeclared, JavaVersion = javaDeclared ? DataScalars.String(javaVersion!) : "" };
    }

    public static ProjectCatalog Discover(string registeredRoot) => Discover(registeredRoot, null);
    public static ProjectCatalog Discover(string registeredRoot, ProjectCatalogLimits? limits, CancellationToken cancellation = default)
    {
        if (limits is not null && (limits.MaximumDirectories <= 0 || limits.MaximumProjects <= 0 || limits.MaximumSourceBytes <= 0))
            throw new DomainException("Project.InvalidCatalogPolicy", "Project catalog quotas must be positive", "use bounded project catalog quotas");
        var root = Path.GetFullPath(registeredRoot);
        var entries = 0; var found = new List<ProjectContext>(); long sourceBytes = 0;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden, IgnoreInaccessible = false }))
        {
            cancellation.ThrowIfCancellationRequested();
            if (limits is not null && ++entries > limits.MaximumDirectories) throw new DomainException("Project.CatalogLimit", "Project discovery exceeds its directory quota", "use bounded project catalog quotas", "LimitsExceeded", "MaximumDirectories");
            if (!File.Exists(Path.Combine(directory, ".modpack", "project.psd1"))) continue;
            if (limits is not null && found.Count >= limits.MaximumProjects) throw new DomainException("Project.CatalogLimit", "Project discovery exceeds its project quota", "use bounded project catalog quotas", "LimitsExceeded", "MaximumProjects");
            if (limits is not null)
                foreach (var relative in new[] { ".modpack/project.psd1", "pack.toml" })
                {
                    var path = Path.Combine(directory, relative);
                    Transactions.WindowsProjectIdentity.GuardAncestors(path);
                    if (!File.Exists(path)) continue;
                    var length = new FileInfo(path).Length;
                    if (length > limits.MaximumSourceBytes - sourceBytes) throw new DomainException("Project.CatalogLimit", "Project discovery exceeds its aggregate source byte quota", "use bounded project catalog quotas", "LimitsExceeded", "MaximumSourceBytes");
                    sourceBytes += length;
                }
            found.Add(Read(directory));
        }
        var projects = found.ToArray();
        var duplicates = projects.GroupBy(project => project.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToArray();
        if (duplicates.Length != 0)
        {
            var details = duplicates.Select(group => $"'{group.Key}': " + string.Join(", ", group.Select(project => project.Root))).ToArray();
            throw new DomainException("Project.DuplicateId", "Multiple registered projects use the same ID", "assign a unique Id in each .modpack/project.psd1 file", "InvalidData", targetData: details.Length == 1 ? new TextTarget(details[0]) : new TextListTarget(details), details: string.Join("; ", details));
        }
        Array.Sort(projects, (left, right) => StringComparer.Create(CultureInfo.CurrentCulture, true).Compare(left.Id, right.Id));
        return new(root, projects);
    }
}
