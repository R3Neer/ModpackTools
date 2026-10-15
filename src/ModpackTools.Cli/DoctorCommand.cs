using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Diagnostics;

namespace ModpackTools.Cli;

internal static class DoctorCommand
{
    public static async Task RunAsync(string command, string[] arguments, string? requestedProject, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
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
        if (switches.Contains("fix")) throw new DomainException("Core.CapabilityUnavailable", "Native doctor repair is not available in this checkpoint", "use diagnostic doctor without --fix");
        var state = Environment.GetEnvironmentVariable("MODPACKTOOLS_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state)) state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools", "native-state");
        var report = await Doctor.ReadAsync(Path.GetFullPath(configurationDirectory), Path.GetFullPath(state), AppContext.BaseDirectory,
            requestedProject, activeProject, DateTimeOffset.UtcNow, Environment.ProcessPath!, switches.Contains("details"), cancellation: cancellation).ConfigureAwait(false);
        using var output = new NativeOutputBuffer("Doctor", "reduce diagnostic detail before retrying");
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
            writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndObject();
        }
        cancellation.ThrowIfCancellationRequested(); Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
    }
}
