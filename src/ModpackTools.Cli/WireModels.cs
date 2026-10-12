using System.Text.Json.Serialization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;

namespace ModpackTools.Cli;

internal sealed record VersionData(VersionInfo Version);
internal sealed record VersionEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, VersionData Data);
internal sealed record PublicProject(string Id, string? Name, string? Version, string? Minecraft, string? Loader, string? LoaderVersion, string Root, string OutputName)
{
    public static PublicProject From(ProjectContext project) => new(project.Id, project.Name, project.Version, project.Minecraft, project.Loader, project.LoaderVersion, project.Root, project.OutputName);
}
internal sealed record ProjectList(string Root, PublicProject[] Items);
internal sealed record ProjectListData(ProjectList Projects);
internal sealed record ProjectListEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, ProjectListData Data);
internal sealed record SessionData(string? ActiveProject);
internal sealed record SessionEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, SessionData Data);
internal sealed record FailureEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, Failure Error)
{
    public static FailureEnvelope Create(string command, string[] arguments, Failure error) => new(1, false, command, arguments, error);
}
internal sealed record Failure(string Id, string Message, string Category, JsonElement Target)
{
    public static Failure From(string id, string message, string category, DiagnosticTarget? target) => new(id, message, category, target switch
    {
        TextTarget text => JsonSerializer.SerializeToElement(text.Value, WireJsonContext.Default.String),
        NumberTarget number => JsonSerializer.SerializeToElement(number.Value, WireJsonContext.Default.Int64),
        DecimalTarget number => JsonSerializer.SerializeToElement(number.Value, WireJsonContext.Default.Decimal),
        FloatingTarget number => JsonSerializer.SerializeToElement(number.Value, WireJsonContext.Default.Double),
        BooleanTarget flag => JsonSerializer.SerializeToElement(flag.Value, WireJsonContext.Default.Boolean),
        TextListTarget items => JsonSerializer.SerializeToElement(items.Values, WireJsonContext.Default.StringArray),
        null => JsonSerializer.SerializeToElement((string?)null, WireJsonContext.Default.String),
        _ => throw new InvalidOperationException("Unknown diagnostic target")
    });
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(VersionEnvelope))]
[JsonSerializable(typeof(FailureEnvelope))]
[JsonSerializable(typeof(ProjectListEnvelope))]
[JsonSerializable(typeof(SessionEnvelope))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string[]))]
internal partial class WireJsonContext : JsonSerializerContext;
