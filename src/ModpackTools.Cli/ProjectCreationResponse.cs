using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core;
using ModpackTools.Core.Projects;

namespace ModpackTools.Cli;

internal sealed record ProjectCreationPresentation(string Kind, PublicProject Project,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PublicTransactionDiagnostic[]? Diagnostics = null);
internal sealed record ProjectCreationEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, EmptyData Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProjectCreationPresentation? Presentation = null);

internal sealed class ProjectCreationResponse(string command, string[] arguments, string root, bool includePresentation) : IDisposable
{
    private readonly NativeOutputBuffer output = new("Project creation", "reduce creation arguments");
    private PublicProject? project;
    private bool prepared;

    public ValueTask PrepareAsync(ProjectCreationResult result, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (includePresentation) project = PublicProject.From(result.Project);
        Serialize(null);
        // Reserve before publication for sixteen bounded diagnostics, including
        // exact Windows paths at worst-case JSON escaping. Reuse this buffer.
        if (output.Length > 56L * 1024 * 1024)
            throw new DomainException("Core.OutputLimit", "Project creation output leaves no diagnostic reserve", "reduce creation arguments", "LimitsExceeded");
        if (includePresentation) output.Capacity = checked((int)output.Length + 8 * 1024 * 1024);
        prepared = true;
        return ValueTask.CompletedTask;
    }

    public void Write(ProjectCreationResult result)
    {
        if (!prepared) throw new InvalidOperationException("Project creation output was not prepared.");
        if (includePresentation && !result.Diagnostics.IsEmpty) Serialize(ResponseDiagnostics.Bounded(result.Diagnostics, root));
        // Publication is terminal. Do not reread the project or check cancellation.
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
    }

    private void Serialize(PublicTransactionDiagnostic[]? diagnostics)
    {
        output.SetLength(0); output.Position = 0;
        using var writer = new Utf8JsonWriter(output);
        JsonSerializer.Serialize(writer, new ProjectCreationEnvelope(1, true, command, arguments, new(),
            includePresentation ? new("project-creation", project!, diagnostics) : null), WireJsonContext.Default.ProjectCreationEnvelope);
    }
    public void Dispose() => output.Dispose();
}
