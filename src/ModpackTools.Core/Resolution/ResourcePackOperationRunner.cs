using System.Collections.Immutable;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public enum ResourcePackOperation { Enable, Move, Disable }
public sealed record ResourcePackOperationRequest(ResourcePackOperation Operation, ImmutableArray<string> Selectors, int Position = 0, bool DryRun = false);

public sealed record ResourcePackOperationResult(ImmutableArray<string> Order, ResourcePackView Resources);

public static class ResourcePackOperationRunner
{
    public static Task<TransactionResult<ResourcePackOperationResult>> ExecuteAsync(ProjectContext project, ResourcePackOperationRequest request,
        string configurationDirectory, string stateDirectory, DateTimeOffset now, TransactionLimits? limits = null,
        TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Func<TransactionPreview<ResourcePackOperationResult>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Operation) || request.Selectors.IsDefaultOrEmpty || request.Selectors.Length > 1024
            || request.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > 4096))
            throw new DomainException("ResourcePack.InvalidRequest", "Resource operation requires bounded nonempty selectors", "select installed resource packs");
        var selectors = BatchSelectorResolver.ResolveFromDirectory(request.Selectors, configurationDirectory, project.Id, now, new(["resourcepack"]));
        return ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: request.DryRun,
            expected: expected, limits: limits, cancellation: cancellation, prepareOutput: prepareOutput);

        Task<ResourcePackOperationResult> Prepare(ProjectContext stage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = InventoryReader.Read(stage);
            _ = DefaultOptionsDetector.AssertInstalled(DefaultOptionsDetector.Read(snapshot), stage.Id);
            var targets = ResourcePackSelector.Resolve(snapshot, selectors)
                .DistinctBy(item => item.DefaultId, StringComparer.InvariantCultureIgnoreCase).ToImmutableArray();
            if (request.Operation == ResourcePackOperation.Move && targets.Any(item => !item.Active))
                throw new DomainException("ResourcePack.NotEnabled", "Every resource pack in a move must already be enabled", "use resource-pack enable for disabled packs", "InvalidOperation");
            var selected = targets.Select(item => item.DefaultId).ToHashSet(StringComparer.InvariantCultureIgnoreCase);
            var order = snapshot.ResourceView.Active.Where(item => !selected.Contains(item.Id)).Select(item => item.Id).ToList();
            if (request.Operation != ResourcePackOperation.Disable)
            {
                if (request.Position < 1 || request.Position > order.Count + 1)
                    throw new DomainException("ResourcePack.InvalidPosition", $"Block position must be between 1 and {order.Count + 1}", "choose a position in the list after removing the selected packs");
                order.InsertRange(request.Position - 1, targets.Select(item => item.DefaultId));
            }
            token.ThrowIfCancellationRequested();
            ResourceOrderEditor.Write(stage, order, snapshot.ResourceView.Active.Select(item => item.Id));
            var ids = order.ToImmutableArray();
            var physical = snapshot.Resources.Select(item => item.MetadataPath is { } path
                ? item with { MetadataPath = TransactionFiles.Within(project.Root, Path.GetRelativePath(stage.Root, path)) } : item).ToImmutableArray();
            return Task.FromResult(new ResourcePackOperationResult(ids, ResourcePackActivation.Compose(physical, ids, snapshot.Metadata)));
        }
    }
}
