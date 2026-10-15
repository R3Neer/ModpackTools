using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Build;
using ModpackTools.Core.Data;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal static class DiffCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        foreach (var token in arguments)
            if (token.StartsWith("--", StringComparison.Ordinal)) throw new DomainException("Option.Unknown", $"Option '--{token[2..].Split('=')[0]}' is not recognized for this command", "run the command with --help", target: token[2..].Split('=')[0]);
        if (arguments.Length != 0) throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack diff",
            targetData: arguments.Length == 1 ? new TextTarget(arguments[0]) : new TextListTarget(arguments));
        var effective = ProjectSelection.EffectiveId(requestedProject, activeProject); configurationDirectory = Path.GetFullPath(configurationDirectory);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effective);
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        using var output = new NativeOutputBuffer("Diff", "reduce the compared project");
        var result = await ProjectDiff.CompareAsync(project, configurationDirectory, Path.GetFullPath(state), ResolveTool, Environment.ProcessPath!,
            cancellation: cancellation, prepareOutput: Prepare).ConfigureAwait(false);
        if (!result.Diagnostics.IsEmpty) Serialize(result.Result.Comparison, ResponseDiagnostics.Bounded(result.Diagnostics, project.Root));
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
        ValueTask Prepare(TransactionPreview<ProjectDiffResult> preview, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Serialize(preview.Result.Comparison, null);
            if (output.Length > 60L * 1024 * 1024) throw new DomainException("Core.OutputLimit", "Diff output leaves no diagnostic reserve", "reduce the compared project", "LimitsExceeded");
            return ValueTask.CompletedTask;
        }
        void Serialize(MrpackComparison comparison, PublicTransactionDiagnostic[]? diagnostics)
        {
            output.SetLength(0); output.Position = 0; using var writer = new Utf8JsonWriter(output);
            writer.WriteStartObject(); writer.WriteNumber("schema_version", 1); writer.WriteBoolean("ok", true); writer.WriteString("command", command);
            writer.WriteStartArray("arguments"); foreach (var argument in arguments) writer.WriteStringValue(argument); writer.WriteEndArray();
            writer.WriteStartObject("data"); writer.WriteStartObject("diff"); writer.WriteNumber("total", comparison.Total);
            Rows("added", comparison.Added.Select(item => (item.Kind, item.Path)).ToArray());
            Rows("changed", comparison.Changed.Select(item => (item.Kind, item.Path)).ToArray());
            Rows("removed", comparison.Removed.Select(item => (item.Kind, item.Path)).ToArray());
            writer.WriteEndObject();
            if (diagnostics is not null)
            {
                writer.WriteStartArray("diagnostics"); foreach (var diagnostic in diagnostics)
                { writer.WriteStartObject(); writer.WriteString("code", diagnostic.Code); writer.WriteString("path", diagnostic.Path); writer.WriteString("message", diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray();
            }
            writer.WriteEndObject(); writer.WriteEndObject();
            void Rows(string name, (string Kind, string Path)[] items)
            {
                writer.WritePropertyName(name);
                // Reproduce the released renderer hook's empty scalar projection
                // only at this public boundary; the domain has no phantom records.
                if (items.Length == 0) { writer.WriteStartObject(); writer.WriteNull("kind"); writer.WriteNull("path"); writer.WriteEndObject(); return; }
                if (items.Length > 1) writer.WriteStartArray();
                foreach (var item in items) { writer.WriteStartObject(); writer.WriteString("kind", item.Kind); writer.WriteString("path", item.Path); writer.WriteEndObject(); }
                if (items.Length > 1) writer.WriteEndArray();
            }
        }
        PackwizObservation ResolveTool() => PackwizRefresh.Resolve(ConfigurationReader.ReadPackwizPath(configurationDirectory), Environment.GetEnvironmentVariable("PATH"),
            Path.Combine(configurationDirectory, "tools", "packwiz", "dfd8b68", "packwiz.exe"), "dfd8b68");
    }
}
