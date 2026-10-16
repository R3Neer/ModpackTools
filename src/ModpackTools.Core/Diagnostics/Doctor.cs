using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ModpackTools.Core.Build;
using ModpackTools.Core.Data;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Diagnostics;

public static partial class Doctor
{
    public static async Task<DoctorReport> ReadAsync(string configurationDirectory, string stateDirectory, string applicationDirectory,
        string? requestedProject, string? activeProject, DateTimeOffset now, string nativeHost, bool details = false,
        DoctorLimits? limits = null, CancellationToken cancellation = default)
    {
        limits ??= new();
        if (limits.MaximumProjects <= 0 || limits.MaximumDirectories <= 0 || limits.MaximumDisplayItems <= 0 || limits.MaximumDisplayCharacters <= 0)
            throw new DomainException("Doctor.InvalidPolicy", "Doctor resource quotas must be positive", "use bounded diagnostic quotas");
        try { return await ReadCore().ConfigureAwait(false); }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Diagnostics were cancelled", "retry when ready", "OperationStopped"); }

        async Task<DoctorReport> ReadCore()
        {
            cancellation.ThrowIfCancellationRequested(); var checks = ImmutableArray.CreateBuilder<DoctorCheck>();
            configurationDirectory = Path.GetFullPath(configurationDirectory); applicationDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory));
            void Add(string section, string status, string label, string value, string? detail = null, ImmutableArray<DoctorItem> items = default)
                => checks.Add(new(section, status, label, value, detail ?? "", items.IsDefault ? [] : items));
            Add("SYSTEM", "pass", "ModpackTools", ProductVersion.Current.Version, applicationDirectory);
            var writable = Probe(configurationDirectory);
            Add("SYSTEM", writable.Writable ? "pass" : "fail", "Configuration", writable.CleanupFailure is not null ? "Probe cleanup failed" : writable.Writable ? "Writable" : "Not writable",
                writable.CleanupFailure ?? Path.Combine(configurationDirectory, "config.psd1"));
            PackwizObservation? packwiz = null;
            try
            {
                packwiz = PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
                    Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68");
            }
            catch (Exception error) when (Recoverable(error)) { Add("PACKWIZ", "fail", "Executable", "Configuration is invalid", error.Message); }
            if (packwiz is not null)
            {
                if (!packwiz.Available) Add("PACKWIZ", "fail", "Executable", packwiz.Source == "configured" ? "Configured path is missing" : "Not found", packwiz.Path);
                else
                {
                    Add("PACKWIZ", "pass", "Executable", packwiz.Path!, "Source: " + packwiz.Source);
                    if (packwiz.Version is not null) Add("PACKWIZ", "pass", "Managed build", packwiz.Version);
                    var working = false;
                    try { _ = await NativeTool.RunAsync(packwiz.Path!, ["--help"], Path.GetTempPath(), nativeHost, new(10000, 64 * 1024), cancellation).ConfigureAwait(false); working = true; }
                    catch (Exception error) when (Recoverable(error)) { }
                    Add("PACKWIZ", working ? "pass" : "fail", "Invocation", working ? "Working" : "Failed");
                }
            }
            ProjectCatalog? catalog = null; string? projectError = null;
            try
            {
                var root = ConfigurationReader.ReadRoot(configurationDirectory); Add("PROJECT ROOT", "pass", "Root", root);
                try
                {
                    catalog = ProjectReader.Discover(root, new(limits.MaximumDirectories, limits.MaximumProjects), cancellation);
                    Add("PROJECT ROOT", "pass", "Discovery", catalog.Projects.Length == 1 ? "1 project discovered" : $"{catalog.Projects.Length} projects discovered");
                }
                catch (Exception error) when (Recoverable(error)) { catalog = null; projectError = error.Message; Add("PROJECT ROOT", "fail", "Discovery", "Failed", error.Message); }
            }
            catch (DomainException error) when (Recoverable(error))
            {
                projectError = error.Message;
                Add("PROJECT ROOT", "fail", "Root", error.Id switch { "ModpackTools.Configuration.MissingRoot" => "Not configured", "ModpackTools.Configuration.RootNotFound" => "Directory does not exist", _ => "Configuration is invalid" },
                    error.Target is TextTarget target ? target.Value : error.Id == "ModpackTools.Configuration.MissingRoot" ? null : error.Message);
            }
            Add("PROJECT ROOT", "info", "Active", string.IsNullOrEmpty(activeProject) ? "None in this session" : activeProject);
            ProjectContext? selected = null; ProjectHealthObservation? health = null; Exception? healthError = null;
            if (!string.IsNullOrWhiteSpace(requestedProject) || !string.IsNullOrWhiteSpace(activeProject))
            {
                if (catalog is not null)
                    try { selected = ProjectSelection.Find(catalog, ProjectSelection.EffectiveId(requestedProject, activeProject)); }
                    catch (Exception error) when (Recoverable(error)) { projectError = error.Message; }
                if (selected is not null)
                    try { health = await ProjectHealth.ReadAsync(selected, configurationDirectory, now, limits: limits.Health, cancellation: cancellation).ConfigureAwait(false); }
                    catch (Exception error) when (Recoverable(error)) { healthError = error; }
            }
            var git = FindExecutable("git.exe");
            if (git is null) Add("OPTIONAL", "warn", "Git", "Not found");
            else
            {
                var version = "Found";
                try { version = GitPrefix().Replace((await NativeTool.RunAsync(git, ["--version"], Path.GetTempPath(), nativeHost, new(10000, 16 * 1024), cancellation).ConfigureAwait(false)).Stdout.TrimEnd('\r', '\n'), ""); }
                catch (Exception error) when (Recoverable(error)) { }
                Add("OPTIONAL", "pass", "Git", version, git);
            }
            var projects = catalog?.Projects ?? []; var missingMod = new List<string>(); var missingConfig = new List<string>(); var unreadable = new List<string>(); var ready = 0;
            foreach (var project in projects)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var snapshot = selected?.Root == project.Root && health is not null ? health.Inventory : InventoryReader.Read(project, limits.Health?.Inventory);
                    var status = DefaultOptionsDetector.Read(snapshot);
                    if (status.Ready) ready++; else if (!status.Installed) missingMod.Add(project.Id); else missingConfig.Add(project.Id);
                }
                catch (Exception error) when (Recoverable(error)) { unreadable.Add(project.Id); }
            }
            if (projects.Length == 0) Add("OPTIONAL", "info", "Default Options", "No projects checked", "Required only for resource pack activation and ordering.");
            else
            {
                var parts = new List<string>();
                if (missingMod.Count != 0) parts.Add("Missing mod: " + string.Join(", ", missingMod) + ".");
                if (missingConfig.Count != 0) parts.Add("Missing config: " + string.Join(", ", missingConfig) + ".");
                if (unreadable.Count != 0) parts.Add("Could not inspect: " + string.Join(", ", unreadable) + ".");
                parts.Add("Required only for modpack resource-pack enable, move, and disable.");
                Add("OPTIONAL", ready == projects.Length ? "pass" : "warn", "Default Options", $"{ready}/{projects.Length} projects ready", string.Join(" ", parts));
            }
            checks.Add(PresentationDependency.Read(applicationDirectory, cancellation)); checks.Add(Minecraft(cancellation));
            if (selected is not null)
            {
                foreach (var relative in new[] { "pack.toml", selected.IndexFile, ".modpack/project.psd1", ".modpack/metadata.psd1" })
                { cancellation.ThrowIfCancellationRequested(); var present = File.Exists(TransactionFiles.Within(selected.Root, relative)); Add("PROJECT", present ? "pass" : "fail", relative, present ? "Present" : "Missing"); }
                try
                {
                    var metadata = health?.Inventory.Metadata ?? EditorialMetadataReader.Read(selected);
                    if (metadata.Mods.Entries.Any(pair => pair.Value is not PsdTable))
                        throw new FormatException("Editorial mod assignments must be tables.");
                    var invalid = metadata.Mods.Entries.Where(pair => pair.Value is PsdTable table && table.Entries.TryGetValue("Category", out var category)
                        && !metadata.Categories.Entries.ContainsKey(EditorialProjection.Display(category))).Select(pair => pair.Key).ToArray();
                    Add("PROJECT", invalid.Length == 0 ? "pass" : "fail", "Editorial metadata", invalid.Length == 0 ? "Valid" : "Unknown category assignments: " + string.Join(", ", invalid));
                }
                catch (Exception error) when (Recoverable(error)) { Add("PROJECT", "fail", "Metadata", error.Message); }
                var index = ProjectIndex.Read(selected, cancellation: cancellation); Add("PROJECT", index.Valid ? "pass" : "fail", "Index", index.Valid ? "Hashes match" : string.Join("; ", index.Issues));
                if (health is not null)
                {
                    var report = health.Report;
                    Add("PROJECT", !report.Errors.IsEmpty ? "fail" : !report.Unknown.IsEmpty || !report.Warnings.IsEmpty ? "warn" : "pass", "Dependencies",
                        !report.Errors.IsEmpty ? $"{report.Errors.Length} conflict(s)" : !report.Unknown.IsEmpty ? "Verification incomplete" : !report.Warnings.IsEmpty ? $"{report.Warnings.Length} warning(s)" : "Healthy",
                        items: HealthDisplay.Read(report, details, limits));
                }
                else Add("PROJECT", "fail", "Dependencies", healthError?.Message ?? "Project health could not be read");
                checks.Add(await Artifact(selected, configurationDirectory, stateDirectory, packwiz, nativeHost, limits, cancellation).ConfigureAwait(false));
            }
            else if (projectError is not null && (!string.IsNullOrWhiteSpace(requestedProject) || !string.IsNullOrWhiteSpace(activeProject))) Add("PROJECT", "fail", "Project", projectError);
            cancellation.ThrowIfCancellationRequested(); return new(checks.ToImmutable());
        }
    }
    internal static bool Recoverable(Exception error) => error is DomainException domain ? domain.Id != "ModpackTools.Core.OperationCancelled" && domain.Data["ModpackTools.RetainStage"] is not true
        : error is IOException or UnauthorizedAccessException or FormatException or ArgumentException;
    private static string? FindExecutable(string filename)
    {
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Take(10000))
            try { var path = Path.GetFullPath(Path.Combine(entry.Trim('"'), filename)); if (File.Exists(path)) return path; } catch (Exception error) when (error is ArgumentException or NotSupportedException) { }
        return null;
    }
    private static (bool Writable, string? CleanupFailure) Probe(string directory)
    {
        var path = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N")); var owned = false; var writable = false; string? cleanup = null;
        try
        {
            WindowsProjectIdentity.GuardAncestors(path); Directory.CreateDirectory(directory); WindowsProjectIdentity.GuardAncestors(path);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { owned = true; file.Write("test"u8); file.Flush(); } writable = true;
        }
        catch (Exception error) when (Recoverable(error)) { }
        finally
        {
            if (owned)
                try { WindowsProjectIdentity.GuardAncestors(path); File.Delete(path); }
                catch (Exception error) when (Recoverable(error)) { cleanup = "Could not remove own configuration probe '" + path + "': " + error.Message; writable = false; }
        }
        return (writable, cleanup);
    }
    [GeneratedRegex(@"^git version\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GitPrefix();
}
