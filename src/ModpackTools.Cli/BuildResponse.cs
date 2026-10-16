using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Build;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal sealed class BuildResponse(string command, string[] arguments, string root, bool includePresentation = false) : IDisposable
{
    private NativeOutputBuffer? buffer;
    private BuildResult? prepared;
    private BuildPresentation? presentation;
    private PublicInventoryItem[] resources = [];
    public ValueTask PrepareAsync(TransactionPreview<BuildResult> preview, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); prepared = preview.Result;
        if (includePresentation) presentation = BuildPresentation.From(prepared);
        if (!prepared.DryRun) resources = ResourcePackCommand.Rows(prepared.Inventory.ResourceView);
        Serialize([]);
        var reserve = includePresentation ? 8 * 1024 * 1024 : 4 * 1024 * 1024;
        if (buffer!.Length > 64L * 1024 * 1024 - reserve) throw new DomainException("Core.OutputLimit", "Build output leaves no diagnostic reserve", "reduce project metadata", "LimitsExceeded");
        buffer.Capacity = checked((int)buffer.Length + reserve);
        return ValueTask.CompletedTask;
    }
    public void Write(TransactionResult<BuildResult> result)
    {
        if (buffer is null || prepared is null) throw new InvalidOperationException("Build output was not prepared.");
        if (!result.Diagnostics.IsEmpty) Serialize(result.Diagnostics);
        Console.OpenStandardOutput().Write(buffer.GetBuffer(), 0, checked((int)buffer.Length)); Console.WriteLine();
    }
    private void Serialize(ImmutableArray<TransactionDiagnostic> diagnostics)
    {
        var result = prepared!; buffer ??= new("Build", "reduce project metadata before building");
        buffer.SetLength(0); buffer.Position = 0;
        using var writer = new Utf8JsonWriter(buffer, new() { MaxDepth = 256 });
        writer.WriteStartObject(); writer.WriteNumber("schema_version", 1); writer.WriteBoolean("ok", true); writer.WriteString("command", command);
        writer.WriteStartArray("arguments"); foreach (var argument in arguments) writer.WriteStringValue(argument); writer.WriteEndArray();
        writer.WriteStartObject("data"); writer.WriteStartObject("health");
        ContentListCommand.Issues(writer, "errors", result.Health.Errors); ContentListCommand.Issues(writer, "warnings", result.Health.Warnings); ContentListCommand.Issues(writer, "unknown", result.Health.Unknown); writer.WriteEndObject();
        if (!result.DryRun)
        {
            writer.WriteStartArray("resource_packs"); foreach (var resource in resources)
                JsonSerializer.Serialize(writer, resource, WireJsonContext.Default.PublicInventoryItem);
            writer.WriteEndArray(); writer.WriteStartObject("build"); writer.WriteString("path", result.Path); writer.WriteNumber("size_bytes", result.Size);
            writer.WriteNumber("duration_seconds", result.Duration.TotalSeconds); writer.WriteBoolean("dry_run", false);
            writer.WriteNumber("mods", result.Inventory.Mods.Length); writer.WriteNumber("client_only", result.Inventory.Mods.Count(item => item.Side == "client"));
            writer.WriteNumber("host_only", result.Inventory.Mods.Count(item => item.Side == "server")); writer.WriteNumber("both", result.Inventory.Mods.Count(item => item.Side == "both"));
            writer.WriteNumber("resources_enabled", result.Inventory.ResourceView.Active.Length); writer.WriteNumber("shaders", result.Inventory.Shaders.Length); writer.WriteEndObject();
        }
        if (!diagnostics.IsEmpty)
        {
            writer.WriteStartArray("diagnostics"); foreach (var diagnostic in ResponseDiagnostics.Bounded(diagnostics, root))
            { writer.WriteStartObject(); writer.WriteString("code", diagnostic.Code); writer.WriteString("path", diagnostic.Path); writer.WriteString("message", diagnostic.Message); writer.WriteEndObject(); }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        if (presentation is not null)
        {
            writer.WritePropertyName("presentation");
            JsonSerializer.Serialize(writer, presentation, WireJsonContext.Default.BuildPresentation);
        }
        writer.WriteEndObject();
    }
    public void Dispose() => buffer?.Dispose();
}
