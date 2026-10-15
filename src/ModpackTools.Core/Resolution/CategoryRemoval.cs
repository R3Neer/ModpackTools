using System.Collections.Immutable;
using System.Text;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public sealed record CategoryRemovalRequest(ImmutableArray<string> Selectors, bool ClearAssignments = false, bool DryRun = false);

public static class CategoryRemoval
{
    public static async Task<TransactionResult<ImmutableArray<string>>> ExecuteAsync(ProjectContext project, CategoryRemovalRequest request,
        string configurationDirectory, string stateDirectory, DateTimeOffset now, TransactionLimits? limits = null,
        TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Func<TransactionPreview<ImmutableArray<string>>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Selectors.IsDefaultOrEmpty || request.Selectors.Length > 1024 || request.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > 4096))
            throw new DomainException("Metadata.InvalidRemovalRequest", "Category removal requires bounded nonempty selectors", "select defined categories");
        var config = MutationConfiguration.Resolve(project, configurationDirectory);
        EditorialMetadata? metadata = null; CachedCategorySnapshot? cache = null; var loaded = false;
        var resolved = ImmutableArray.CreateBuilder<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selector in request.Selectors)
        {
            cancellation.ThrowIfCancellationRequested();
            if (selector.Equals("unclassified", StringComparison.OrdinalIgnoreCase))
                _ = CategorySelector.ResolveFromProject(project.Root, project.Id, config, selector, now);
            metadata ??= EditorialMetadataReader.Read(project);
            if (ReferenceCacheResolver.IsNumericSelector(selector) && !loaded) { cache = CategoryCacheReader.Read(config); loaded = true; }
            var id = CategorySelector.Resolve(metadata, cache, selector, project.Id, now);
            if (seen.Add(id)) resolved.Add(id);
        }
        var categories = resolved.ToImmutable();
        var result = await ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: request.DryRun,
            expected: expected, limits: limits, cancellation: cancellation, prepareOutput: prepareOutput).ConfigureAwait(false);
        return request.DryRun ? result : CategoryCacheRefresh.Apply(result, project, config, now);

        Task<ImmutableArray<string>> Prepare(ProjectContext stage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var source = EditorialMetadataReader.Read(stage); var definitions = source.Categories.Entries; var mods = source.Mods.Entries;
            Dictionary<string, List<string>>? assignments = null;
            foreach (var id in categories)
            {
                token.ThrowIfCancellationRequested();
                _ = CategorySelector.Resolve(source with { Categories = new(definitions) }, null, id, project.Id, now);
                if (assignments is null)
                {
                    assignments = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var (modId, value) in mods)
                    {
                        if (value is not PsdTable entry) throw new DomainException("Metadata.InvalidOverride", "Category removal requires table-shaped mod overrides", "repair the mod metadata", "InvalidData", modId);
                        if (!entry.Entries.TryGetValue("Category", out var category)) continue;
                        var key = EditorialProjection.Display(category);
                        if (!assignments.TryGetValue(key, out var group)) assignments.Add(key, group = []);
                        group.Add(modId);
                    }
                }
                var assigned = assignments.GetValueOrDefault(id) ?? [];
                if (assigned.Count != 0 && !request.ClearAssignments)
                {
                    assigned.Sort(StringComparer.OrdinalIgnoreCase);
                    throw new DomainException("Metadata.CategoryInUse", $"Category '{id}' is assigned to {assigned.Count} mod(s) and cannot be removed safely",
                        $"modpack category remove {id} --clear-assignments", "InvalidOperation", id, details: string.Join(", ", assigned.Take(8)));
                }
                foreach (var modId in assigned)
                {
                    var entry = ((PsdTable)mods[modId]).Entries.Remove("Category");
                    mods = entry.IsEmpty ? mods.Remove(modId) : mods.SetItem(modId, new PsdTable(entry));
                }
                definitions = definitions.Remove(id);
            }
            var output = new PsdTable(source.Data.Entries.SetItem("Categories", new PsdTable(definitions)).SetItem("Mods", new PsdTable(mods)));
            var path = TransactionFiles.Within(stage.Root, ".modpack/metadata.psd1");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, PsdDataWriter.Write(output, 4 * 1024 * 1024), new UTF8Encoding(false, true));
            var verified = EditorialMetadataReader.Read(stage);
            if (categories.Any(id => verified.Categories.Entries.ContainsKey(id))) throw new DomainException("Metadata.VerificationFailed", "Removed category was retained", "inspect stage metadata", "InvalidResult");
            return Task.FromResult(categories);
        }
    }
}
