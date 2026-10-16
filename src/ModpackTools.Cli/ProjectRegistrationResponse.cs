using System.Text.Json;
using System.Text.Json.Serialization;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal sealed record ProjectRegistrationPresentation(string Kind, PublicProject Project, string[] CreatedFiles,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PublicTransactionDiagnostic[]? Diagnostics = null);
internal sealed record ProjectRegistrationEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, EmptyData Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProjectRegistrationPresentation? Presentation = null);

internal sealed class ProjectRegistrationResponse(string command, string[] arguments, string root, bool includePresentation) : IDisposable
{
    private readonly NativeOutputBuffer output = new("Project registration", "reduce registration arguments");
    private PublicProject? project;
    private string[] createdFiles = [];
    private bool prepared;

    public ValueTask PrepareAsync(TransactionPreview<ProjectRegistrationResult> preview, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (includePresentation)
        {
            project = PublicProject.From(preview.Result.Project);
            createdFiles = preview.Result.CreatedFiles.ToArray();
        }
        Serialize(null);
        // Reserve before publication for sixteen bounded diagnostics, including
        // exact Windows paths at worst-case JSON escaping. Reuse this buffer.
        if (output.Length > 56L * 1024 * 1024)
            throw new DomainException("Core.OutputLimit", "Project registration output leaves no diagnostic reserve", "reduce registration arguments", "LimitsExceeded");
        if (includePresentation) output.Capacity = checked((int)output.Length + 8 * 1024 * 1024);
        prepared = true;
        return ValueTask.CompletedTask;
    }

    public void Write(TransactionResult<ProjectRegistrationResult> result)
    {
        if (!prepared) throw new InvalidOperationException("Project registration output was not prepared.");
        if (includePresentation && !result.Diagnostics.IsEmpty) Serialize(ResponseDiagnostics.Bounded(result.Diagnostics, root));
        // Publication is terminal. Do not reread the project or check cancellation.
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); Console.WriteLine();
    }

    private void Serialize(PublicTransactionDiagnostic[]? diagnostics)
    {
        output.SetLength(0); output.Position = 0;
        using var writer = new Utf8JsonWriter(output);
        JsonSerializer.Serialize(writer, new ProjectRegistrationEnvelope(1, true, command, arguments, new(),
            includePresentation ? new("project-registration", project!, createdFiles, diagnostics) : null), WireJsonContext.Default.ProjectRegistrationEnvelope);
    }
    public void Dispose() => output.Dispose();
}
