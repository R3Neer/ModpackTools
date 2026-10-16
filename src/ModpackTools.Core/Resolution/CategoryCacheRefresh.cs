using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

internal static class CategoryCacheRefresh
{
    public static TransactionResult<T> Apply<T>(TransactionResult<T> result, ProjectContext project, string config, DateTimeOffset now)
    {
        try
        {
            // Cache stores Id/Name/Index, so no further inventory/JAR scan is
            // needed for counts. Retain the established CategoryList order.
            var view = CategoryList.Compose(project, CategoryList.Prepare(EditorialMetadataReader.Read(project)), []);
            _ = CategoryReferenceCache.Write(config, view, now);
        }
        catch (Exception error) when (error is DomainException or IOException or UnauthorizedAccessException or FormatException)
        {
            return result with
            {
                Diagnostics = result.Diagnostics.Add(new("Metadata.CategoryCacheRefreshFailed",
                Path.Combine(config, "last-categories.json"), error.Message))
            };
        }
        return result;
    }
}
