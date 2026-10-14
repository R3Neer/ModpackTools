using System.Collections.Immutable;
using ModpackTools.Core.Projects;

namespace ModpackTools.Core.Inventory;

public sealed record InventoryReadLimits(ManagedReadLimits? Managed = null, LocalJarReadLimits? LocalMods = null,
    LocalZipReadLimits? LocalZips = null, ResourceOrderReadLimits? ResourceOrder = null);

public sealed record RawInventorySnapshot(ProjectContext Project, EditorialMetadata Metadata,
    ImmutableArray<InventoryItem> Mods, ImmutableArray<InventoryItem> Resources, ResourcePackView ResourceView,
    ImmutableArray<InventoryItem> Shaders, ImmutableArray<LocalModObservation> LocalModObservations);

public static class InventoryReader
{
    public static RawInventorySnapshot Read(ProjectContext project, InventoryReadLimits? limits = null)
        => ReadCore(project, null, limits);

    internal static RawInventorySnapshot Read(ProjectContext project, EditorialMetadata metadata, InventoryReadLimits? limits)
        => ReadCore(project, metadata, limits);

    internal static RawInventorySnapshot ReadForHealth(ProjectContext project, InventoryReadLimits? limits) => ReadCore(project, null, limits, true);

    private static RawInventorySnapshot ReadCore(ProjectContext project, EditorialMetadata? suppliedMetadata, InventoryReadLimits? limits, bool installFacts = false)
    {
        limits ??= new();
        var managedBudget = new ReadBudget(limits.Managed ?? new());
        var zipBudget = new LocalZipBudget(limits.LocalZips ?? new());
        var localPolicy = limits.LocalMods ?? new();
        localPolicy.Validate();
        var orderPolicy = limits.ResourceOrder ?? new();
        orderPolicy.Validate();

        var metadata = suppliedMetadata ?? EditorialMetadataReader.Read(project);
        var managedMods = ManagedContentReader.Read(project, ContentKind.Mod, managedBudget, installFacts);
        var observations = LocalJarReader.Read(project, managedMods, localPolicy);
        var mods = ModClassification.Apply(managedMods.Select(InventoryItem.From)
            .Concat(observations.Select(observation => InventoryItem.From(observation.Item))).ToImmutableArray(), metadata);
        var managedResources = ManagedContentReader.Read(project, ContentKind.ResourcePack, managedBudget, installFacts);
        var resources = managedResources.Select(InventoryItem.From)
            .Concat(LocalZipReader.Read(project, managedResources, ContentKind.ResourcePack, zipBudget).Select(InventoryItem.From)).ToImmutableArray();
        var managedShaders = ManagedContentReader.Read(project, ContentKind.ShaderPack, managedBudget, installFacts);
        var shaders = managedShaders.Select(InventoryItem.From)
            .Concat(LocalZipReader.Read(project, managedShaders, ContentKind.ShaderPack, zipBudget).Select(InventoryItem.From)).ToImmutableArray();
        var view = ResourcePackActivation.Compose(resources, ResourcePackOrderReader.Read(project, orderPolicy), metadata);
        return new(project, metadata, mods.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToImmutableArray(),
            resources, view, shaders.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToImmutableArray(), observations);
    }
}
