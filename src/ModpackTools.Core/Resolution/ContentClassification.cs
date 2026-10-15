using System.Collections.Immutable;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public sealed record ContentClassificationRequest(ImmutableArray<string> Selectors, string Category = "unclassified", bool DryRun = false);

public static class ContentClassification
{
    public static async Task<TransactionResult<string>> ExecuteAsync(ProjectContext project, ContentClassificationRequest request,
        string configurationDirectory, string stateDirectory, DateTimeOffset now, TransactionLimits? limits = null,
        TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Func<TransactionPreview<string>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Selectors.IsDefaultOrEmpty || request.Selectors.Length > 1024
            || request.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > 4096)
            || string.IsNullOrEmpty(request.Category) || request.Category.Length > 4096)
            throw new DomainException("Metadata.InvalidClassificationRequest", "Classification requires bounded nonempty selectors and category", "select installed mods and a defined category");
        var config = MutationConfiguration.Resolve(project, configurationDirectory);
        var category = CategorySelector.ResolveFromProject(project.Root, project.Id, config, request.Category, now, allowUnclassified: true);
        var selectors = BatchSelectorResolver.ResolveFromDirectory(request.Selectors, config, project.Id, now);
        var result = await ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: request.DryRun,
            expected: expected, limits: limits, cancellation: cancellation, prepareOutput: prepareOutput).ConfigureAwait(false);
        if (!request.DryRun) result = CategoryCacheRefresh.Apply(result, project, config, now);
        return result;

        Task<string> Prepare(ProjectContext stage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = InventoryReader.Read(stage);
            var targets = ModClassificationSelector.Resolve(snapshot, selectors)
                .OrderBy(item => item.Id, StringComparer.CurrentCultureIgnoreCase)
                .DistinctBy(item => item.Id, StringComparer.CurrentCultureIgnoreCase).ToImmutableArray();
            RequestedContentOptions.Classify(stage, snapshot, targets, category);
            return Task.FromResult(category);
        }
    }
}
