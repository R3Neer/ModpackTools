using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Diagnostics;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Dependencies;
using ModpackTools.Core.Processes;

namespace ModpackTools.Cli;

internal static class DoctorCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation, DoctorRepairScope scope = DoctorRepairScope.All,
        bool includePresentation = false, string? reviewToken = null)
    {
        var switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var values = new List<string>();
        foreach (var token in arguments)
        {
            if (!token.StartsWith("--", StringComparison.Ordinal)) { values.Add(token); continue; }
            var parts = token[2..].Split('=', 2); var name = parts[0];
            if (!new[] { "fix", "yes", "dry-run", "strict", "allow-downgrade", "details" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (parts.Length != 1) throw new DomainException("Option.UnexpectedValue", $"Option '--{name}' does not accept a value", $"--{name}", target: name);
            switches.Add(name);
        }
        if (values.Count != 0) throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack doctor [--fix] [--yes]",
            targetData: values.Count == 1 ? new TextTarget(values[0]) : new TextListTarget([.. values]));
        if (switches.Contains("yes") && !switches.Contains("fix")) throw new DomainException("Option.RequiredCombination", "Option '--yes' requires '--fix'", "modpack doctor --fix --yes");
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        var observedConfiguration = scope == DoctorRepairScope.Project ? ConfigurationStore.Observe(configurationDirectory) : null;
        DoctorProjectReviewPresentation? presentation = null;
        var report = scope == DoctorRepairScope.Environment ? ReadEnvironment() : await Doctor.ReadAsync(Path.GetFullPath(configurationDirectory), Path.GetFullPath(state), AppContext.BaseDirectory,
            requestedProject, activeProject, DateTimeOffset.UtcNow, Environment.ProcessPath!, switches.Contains("details"), cancellation: cancellation).ConfigureAwait(false);
        var originalReport = report;
        using var output = new NativeOutputBuffer("Doctor", "reduce diagnostic detail before retrying");
        Serialize(report, null);
        if (switches.Contains("fix") && switches.Contains("yes") && !switches.Contains("dry-run"))
        {
            CheckReserve(); output.Capacity = 64 * 1024 * 1024;
        }
        DoctorRepairResult? repair = null;
        if (switches.Contains("fix"))
        {
            ProjectContext? project = null;
            if (scope != DoctorRepairScope.Environment && (!string.IsNullOrWhiteSpace(requestedProject) || !string.IsNullOrWhiteSpace(activeProject)))
            {
                try { project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory), new(100000, 10000), cancellation), ProjectSelection.EffectiveId(requestedProject, activeProject)); }
                catch (DomainException) when (scope != DoctorRepairScope.Project) { /* The default report retains the selection/root failure. */ }
            }
            repair = await DoctorRepair.ExecuteAsync(configurationDirectory, Path.GetFullPath(state), Path.GetTempPath(),
                Environment.ProcessPath!, project, new(switches.Contains("yes"), switches.Contains("dry-run"), switches.Contains("strict"), switches.Contains("allow-downgrade"), scope),
                DateTimeOffset.UtcNow, cancellation, (receipt, token) =>
                {
                    token.ThrowIfCancellationRequested(); Serialize(originalReport, receipt); CheckReserve(); return ValueTask.CompletedTask;
                }, reviewProject: scope == DoctorRepairScope.Project ? ReviewProject : null).ConfigureAwait(false);
            if (repair.Applied && !repair.Diagnostics.Any(value => value.RetainedEvidence))
            {
                try
                {
                    report = scope == DoctorRepairScope.Environment ? ReadEnvironment() : await Doctor.ReadAsync(configurationDirectory, Path.GetFullPath(state), AppContext.BaseDirectory, requestedProject, activeProject,
                    DateTimeOffset.UtcNow, Environment.ProcessPath!, switches.Contains("details"), cancellation: cancellation).ConfigureAwait(false);
                }
                catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or OperationCanceledException)
                { report = report with { Checks = report.Checks.Add(new("SYSTEM", "fail", "Repair readback", "Could not refresh diagnostics after application", error.Message, [])) }; }
            }
            foreach (var diagnostic in repair.Diagnostics)
                report = report with { Checks = report.Checks.Add(new("SYSTEM", "fail", "Repair", diagnostic.Code, diagnostic.Path, [])) };
        }
        try { Serialize(report, repair); }
        catch (DomainException error) when (repair?.Applied == true && error.Id == "ModpackTools.Core.OutputLimit")
        {
            // The prepublication encoding of this baseline and receipt already
            // fitted with reserved space. A larger readback cannot erase success.
            report = originalReport with { Checks = originalReport.Checks.Add(new("SYSTEM", "fail", "Repair readback", "Updated diagnostics exceeded the output quota", "Pre-repair diagnostics follow; the repair receipt records actual publication.", [])) };
            Serialize(report, repair);
        }
        if (repair?.Applied != true) cancellation.ThrowIfCancellationRequested();
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();

        ValueTask ReviewProject(DoctorRepairResult receipt, PackwizObservation tool, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ConfigurationStore.Observe(configurationDirectory) != observedConfiguration)
                throw new DomainException("Transaction.ReviewChanged", "Configuration changed during project repair review; no reviewed project changes were applied",
                    "review the configuration and run doctor --fix again", "ResourceBusy");
            var current = DoctorProjectReviewPresentation.From(receipt, observedConfiguration!, tool,
                switches.Contains("strict"), switches.Contains("allow-downgrade"));
            current.AssertReviewed(reviewToken);
            presentation = current;
            return ValueTask.CompletedTask;
        }
        DoctorReport ReadEnvironment()
        {
            var tool = PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
                Path.Combine(configurationDirectory, "tools", "packwiz", DependencyPackage.Packwiz.Version, "packwiz.exe"), DependencyPackage.Packwiz.Version);
            return new([new("PACKWIZ", tool.Available ? "pass" : "fail", "Executable", tool.Available ? "Available" : "Not found", tool.Path ?? "", [])]);
        }

        void CheckReserve()
        {
            if (output.Length > 48L * 1024 * 1024) throw new DomainException("Core.OutputLimit", "Doctor repair output leaves insufficient diagnostic reserve", "reduce diagnostic detail before applying repair", "LimitsExceeded");
        }
        void Serialize(DoctorReport report, DoctorRepairResult? repair)
        {
            output.SetLength(0); output.Position = 0;
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteNumber("schema_version", 1); writer.WriteBoolean("ok", true); writer.WriteString("command", command);
                writer.WriteStartArray("arguments"); foreach (var argument in arguments) writer.WriteStringValue(argument); writer.WriteEndArray();
                writer.WriteStartObject("data"); writer.WriteStartObject("doctor"); writer.WriteNumber("failures", report.Failures); writer.WriteNumber("warnings", report.Warnings);
                writer.WriteStartArray("checks");
                foreach (var check in report.Checks)
                {
                    writer.WriteStartObject(); writer.WriteString("section", check.Section); writer.WriteString("status", check.Status); writer.WriteString("label", check.Label);
                    writer.WriteString("value", check.Value); writer.WriteString("detail", check.Detail); writer.WriteStartArray("items");
                    foreach (var item in check.Items) { writer.WriteStartObject(); writer.WriteString("Status", item.Status); writer.WriteString("Text", item.Text); writer.WriteEndObject(); }
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndArray();
                if (repair is not null)
                {
                    writer.WriteStartObject("repair"); writer.WriteBoolean("applied", repair.Applied); writer.WriteBoolean("dry_run", switches.Contains("dry-run"));
                    writer.WriteBoolean("preview", !switches.Contains("yes") && !switches.Contains("dry-run"));
                    writer.WriteBoolean("packwiz_installed", repair.PackwizInstalled); writer.WriteString("packwiz_path", repair.PackwizPath);
                    writer.WriteBoolean("configuration_updated", repair.ConfigurationUpdated);
                    writer.WriteBoolean("project_applied", repair.Project?.Applied == true);
                    writer.WriteNumber("change_count", repair.Project?.Changes.Length ?? 0); writer.WriteStartArray("changes");
                    if (repair.Project is not null) foreach (var change in repair.Project.Changes)
                    { writer.WriteStartObject(); writer.WriteString("kind", change.Before is null ? "add" : change.After is null ? "remove" : "change"); writer.WriteString("path", change.Path); writer.WriteEndObject(); }
                    writer.WriteEndArray();
                    writer.WriteNumber("diagnostics_omitted", Math.Max(0, repair.Diagnostics.Length - 15));
                    writer.WriteStartArray("diagnostics");
                    foreach (var diagnostic in repair.Diagnostics.OrderByDescending(value => value.RetainedEvidence).Take(15))
                    {
                        writer.WriteStartObject(); writer.WriteString("code", diagnostic.Code); writer.WriteString("path", diagnostic.Path);
                        writer.WriteString("message", diagnostic.Message[..Math.Min(2048, diagnostic.Message.Length)]);
                        writer.WriteString("details", diagnostic.Details is null ? null : diagnostic.Details[..Math.Min(65536, diagnostic.Details.Length)]);
                        writer.WriteBoolean("retained_evidence", diagnostic.RetainedEvidence);
                        writer.WriteBoolean("truncated", diagnostic.Message.Length > 2048 || diagnostic.Details?.Length > 65536);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndObject(); writer.WriteEndObject();
                if (includePresentation && presentation is not null)
                {
                    writer.WritePropertyName("presentation");
                    JsonSerializer.Serialize(writer, presentation, WireJsonContext.Default.DoctorProjectReviewPresentation);
                }
                writer.WriteEndObject();
            }
        }
    }
}
