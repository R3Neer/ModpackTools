using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

internal static class MutationConfiguration
{
    public static string Resolve(ProjectContext project, string path)
    {
        // Handle-based paths cover DOS short names and drive aliases without
        // links; a missing leaf is resolved from its nearest existing ancestor.
        var config = ProjectTransaction.CanonicalFutureDirectory(path);
        var root = WindowsProjectIdentity.Inspect(project.Root).CanonicalRoot;
        if (StringComparer.OrdinalIgnoreCase.Equals(config, root) || TransactionFiles.IsBelow(config, root))
            throw new DomainException("Transaction.UnsafeConfigurationDirectory", "Mutation configuration/cache must be outside the protected project",
                "choose a separate configuration directory", "InvalidOperation", config);
        return config;
    }
}
