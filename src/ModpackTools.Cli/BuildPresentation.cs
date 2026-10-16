using ModpackTools.Core.Build;
using ModpackTools.Core.Diagnostics;
using ModpackTools.Core.Inventory;

namespace ModpackTools.Cli;

internal sealed record BuildPresentation(string Kind, PublicProject Project, DoctorItem[] HealthItems,
    string[] Log, PublicInventoryItem[] Mods, PublicInventoryItem[] Shaders, InventoryGroupPresentation[] Groups)
{
    public static BuildPresentation From(BuildResult result)
    {
        if (result.DryRun) return new("build", PublicProject.From(result.Inventory.Project),
            [.. HealthDisplay.Read(result.Health, false, new(), compact: true)], [], [], [], []);
        var view = InventoryReferences.Assign(InventorySelector.Select(result.Inventory, new()));
        return new("build", PublicProject.From(result.Inventory.Project),
            [.. HealthDisplay.Read(result.Health, false, new(), compact: true)], [.. result.Log],
            [.. view.Mods.Select(row => Item(row.Item, "mod"))],
            [.. view.Shaders.Select(row => Item(row.Item, "shaderpack"))], InventoryPresentation.Group(view));
    }
    private static PublicInventoryItem Item(InventoryItem item, string kind) => new(null, item.Id, item.Name, kind, item.Side,
        item.Source.ToString().ToLowerInvariant(), item.Filename, item.Category, item.Pinned ?? false, "", null, item.VersionId);
}
