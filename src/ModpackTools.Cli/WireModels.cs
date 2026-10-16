using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Inventory;

namespace ModpackTools.Cli;

internal sealed record EmptyData;
internal sealed record EmptySuccessEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, EmptyData Data);
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
internal sealed record SessionEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, SessionData Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionPresentation? Presentation = null);
internal sealed record PublicCategory(int Number, string Id, string Name, int? Order, int ModCount, bool Unclassified);
internal sealed record PublicCategories(PublicProject Project, PublicCategory[] Items)
{
    public static PublicCategories From(CategoryListView view) => new(PublicProject.From(view.Project),
        view.Categories.Select(row => new PublicCategory(row.ReferenceNumber, row.Id, row.Name, row.Order, row.ModCount, row.IsUnclassified)).ToArray());
}
internal sealed record CategoryListData(PublicCategories Categories,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PublicTransactionDiagnostic[]? Diagnostics = null);
internal sealed record CategoryListEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, CategoryListData Data);
internal sealed record PublicSummary(int Mods, int ResourcesEnabled, int ResourcesDisabled, int Shaders);
internal sealed record ProjectStatusData(PublicProject Project, PublicSummary Summary);
internal sealed record ProjectStatusEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, ProjectStatusData Data);
internal sealed record PublicVersion(int Number, string Id, string Version, string VersionType, string? Filename, string Published, bool Installed)
{
    public static PublicVersion From(CompatibleVersionRow row) => new(row.Number, row.Candidate.Version.Id,
        row.Candidate.Version.VersionNumber, row.Candidate.VersionType, row.Candidate.Filename, row.Candidate.Version.Published.Text, row.Installed);
}
internal sealed record PublicVersionList(string ItemName, string ItemType, PublicVersion[] Items);
internal sealed record VersionListData(PublicProject Project, PublicVersionList Versions);
internal sealed record VersionListEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, VersionListData Data);
internal sealed record PublicSearchHit(int Number, string Id, string Slug, string Name, string Type, string Author, long Downloads, string Description)
{
    public static PublicSearchHit From(SearchHit hit) => new(hit.Number, hit.ProjectId, hit.Slug, hit.Title, hit.Type, hit.Author, hit.Downloads, hit.Description);
}
internal sealed record PublicSearch(string Query, string Type, long TotalHits, PublicSearchHit[] Results);
internal sealed record SearchData(PublicProject? Project, PublicSearch Search);
internal sealed record SearchEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, SearchData Data);
internal sealed record PublicTransactionChange(string Action, string Path, string? Reason);
internal sealed record PublicTransactionDiagnostic(string Code, string Path, string Message);
internal sealed record PublicTransaction(bool Applied, bool DryRun, bool Preview, int ChangeCount, PublicTransactionChange[] Changes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PublicTransactionDiagnostic[]? Diagnostics = null);
internal sealed record PublicInventoryItem(int? Number, string Id, string? Name, string? Type, string? Side, string Source, string? Filename,
    string? Category, bool Pinned, string State, int? Priority, string? VersionId, string? ProjectId = null, string? Slug = null);
internal sealed record TransactionData(PublicTransaction Transaction,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PublicInventoryItem[]? ResourcePacks = null);
internal sealed record TransactionEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, TransactionData Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RemovalPresentation? Presentation = null);
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
[JsonSerializable(typeof(EmptySuccessEnvelope))]
[JsonSerializable(typeof(HelpEnvelope))]
[JsonSerializable(typeof(ConfigurationEnvelope))]
[JsonSerializable(typeof(ProjectCreationEnvelope))]
[JsonSerializable(typeof(InventoryPresentation))]
[JsonSerializable(typeof(DiffPresentation))]
[JsonSerializable(typeof(VersionEnvelope))]
[JsonSerializable(typeof(FailureEnvelope))]
[JsonSerializable(typeof(ProjectListEnvelope))]
[JsonSerializable(typeof(SessionEnvelope))]
[JsonSerializable(typeof(CategoryListEnvelope))]
[JsonSerializable(typeof(ProjectStatusEnvelope))]
[JsonSerializable(typeof(VersionListEnvelope))]
[JsonSerializable(typeof(SearchEnvelope))]
[JsonSerializable(typeof(TransactionEnvelope))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string[]))]
internal partial class WireJsonContext : JsonSerializerContext;
