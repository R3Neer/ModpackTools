using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;
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
internal sealed record PublicCategory(int Number, string Id, string Name, int? Order, int ModCount, bool Unclassified);
internal sealed record PublicCategories(PublicProject Project, PublicCategory[] Items);
internal sealed record CategoryListData(PublicCategories Categories);
internal sealed record CategoryListEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, CategoryListData Data);
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
        DecimalTarget number => FractionalNumber(number.Value.ToString(CultureInfo.InvariantCulture)),
        FloatingTarget number => FractionalNumber(number.Value.ToString("R", CultureInfo.InvariantCulture)),
        BooleanTarget flag => JsonSerializer.SerializeToElement(flag.Value, WireJsonContext.Default.Boolean),
        TextListTarget items => JsonSerializer.SerializeToElement(items.Values, WireJsonContext.Default.StringArray),
        null => JsonSerializer.SerializeToElement((string?)null, WireJsonContext.Default.String),
        _ => throw new InvalidOperationException("Unknown diagnostic target")
    });

    private static JsonElement FractionalNumber(string text)
    {
        using var document = JsonDocument.Parse(text.IndexOfAny(['.', 'e', 'E']) >= 0 ? text : text + ".0");
        return document.RootElement.Clone();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(VersionEnvelope))]
[JsonSerializable(typeof(FailureEnvelope))]
[JsonSerializable(typeof(ProjectListEnvelope))]
[JsonSerializable(typeof(SessionEnvelope))]
[JsonSerializable(typeof(CategoryListEnvelope))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string[]))]
internal partial class WireJsonContext : JsonSerializerContext;
