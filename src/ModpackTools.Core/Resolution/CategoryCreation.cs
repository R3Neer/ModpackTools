using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public sealed record CategoryCreationRequest(string Id, string? Name = null, int? Order = null);

public static class CategoryCreation
{
    public static async Task<TransactionResult<CategoryListView>> ExecuteAsync(ProjectContext project, CategoryCreationRequest request,
        string configurationDirectory, string stateDirectory, DateTimeOffset now, TransactionLimits? limits = null,
        TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Func<TransactionPreview<CategoryListView>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Id) || request.Id.Length > 4096 || request.Name?.Length > 4096)
            throw new DomainException("Metadata.InvalidCreationRequest", "Category creation requires bounded ID and name", "choose a bounded category ID and display name");
        if (!Regex.IsMatch(request.Id, "^[a-z][a-z0-9-]*$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking))
            throw new DomainException("Metadata.InvalidCategoryId", $"Category ID '{request.Id}' is invalid; allowed characters: lowercase letters, numbers, hyphens", "choose an ID such as world-generation", target: request.Id);
        if (request.Id.Equals("unclassified", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Metadata.ReservedCategoryId", "Category ID 'unclassified' is reserved", "choose a different category ID", target: request.Id);
        var config = MutationConfiguration.Resolve(project, configurationDirectory);
        var result = await ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare,
            expected: expected, limits: limits, cancellation: cancellation, prepareOutput: prepareOutput).ConfigureAwait(false);
        return CategoryCacheRefresh.Apply(result, project, config, now);

        Task<CategoryListView> Prepare(ProjectContext stage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var source = EditorialMetadataReader.Read(stage);
            if (source.Categories.Entries.ContainsKey(request.Id)) throw new DomainException("Metadata.CategoryAlreadyExists", $"Category '{request.Id}' already exists in project '{project.Id}'", "modpack category list", "ResourceExists", request.Id);
            var name = string.IsNullOrWhiteSpace(request.Name) ? request.Id.ToUpperInvariant() : request.Name;
            var order = request.Order;
            if (order is null)
            {
                int? maximum = null;
                foreach (var (key, value) in source.Categories.Entries)
                {
                    try
                    {
                        if (!EditorialProjection.TryMemberField(value, "Order", out var field)) continue;
                        var number = DataScalars.Integer(field); maximum = maximum is { } prior ? Math.Max(prior, number) : number;
                    }
                    catch (Exception error) when (error is FormatException or OverflowException)
                    { throw new DomainException("Metadata.InvalidCategoryDefinition", $"Category definition '{key}' cannot supply a valid Order", "repair the category definition", "InvalidData", key, details: error.Message); }
                }
                try { order = checked((maximum ?? 0) + 10); }
                catch (OverflowException) { throw new DomainException("Metadata.CategoryOrderOverflow", "The next automatic category order exceeds a signed 32-bit integer", "choose an explicit --order value", "LimitsExceeded", request.Id); }
            }
            var entryData = new PsdTable(System.Collections.Immutable.ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("Name", new PsdString(name)).Add("Order", new PsdNumber(order.Value.ToString(CultureInfo.InvariantCulture))));
            var data = new PsdTable(source.Data.Entries.SetItem("Categories", new PsdTable(source.Categories.Entries.Add(request.Id, entryData))));
            var path = TransactionFiles.Within(stage.Root, ".modpack/metadata.psd1");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, PsdDataWriter.Write(data, 4 * 1024 * 1024), new UTF8Encoding(false, true));
            var verified = EditorialMetadataReader.Read(stage);
            var definition = CategoryDefinitions.Extract(request.Id, verified.Categories.Entries[request.Id]);
            if (definition.Name != name || definition.Order != order) throw new DomainException("Metadata.VerificationFailed", "Created category did not retain its name and order", "inspect stage metadata", "InvalidResult", request.Id);
            var definitions = CategoryList.Prepare(verified);
            var inventory = InventoryReader.Read(stage, verified, null);
            return Task.FromResult(CategoryList.Compose(project, definitions, inventory.Mods));
        }
    }
}
