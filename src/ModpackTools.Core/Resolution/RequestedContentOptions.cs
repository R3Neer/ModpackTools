using System.Collections.Immutable;
using System.Text;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

internal static class RequestedContentOptions
{
    public static void Classify(ProjectContext stage, ImmutableArray<string> requested, string category)
    {
        if (requested.IsEmpty) return;
        var snapshot = InventoryReader.Read(stage);
        var targets = ModClassificationSelector.Resolve(snapshot, requested);
        Classify(stage, snapshot, targets, category);
    }

    internal static void Classify(ProjectContext stage, RawInventorySnapshot snapshot, ImmutableArray<InventoryItem> targets, string category)
    {
        var metadata = snapshot.Metadata; var mods = metadata.Mods.Entries;
        foreach (var target in targets)
        {
            var entries = mods.TryGetValue(target.Id, out var existing)
                ? (existing as PsdTable ?? throw new DomainException("Metadata.InvalidOverride", "Requested mod override must be a table", "repair the mod metadata", "InvalidData", target.Id)).Entries
                : ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
            if (category == "unclassified")
            {
                entries = entries.Remove("Category"); mods = entries.IsEmpty ? mods.Remove(target.Id) : mods.SetItem(target.Id, new PsdTable(entries));
            }
            else mods = mods.SetItem(target.Id, new PsdTable(entries.SetItem("Category", new PsdString(category))));
        }
        var data = new PsdTable(metadata.Data.Entries.SetItem("Mods", new PsdTable(mods)));
        Write(stage, ".modpack/metadata.psd1", PsdDataWriter.Write(data, 4 * 1024 * 1024));
        var updated = InventoryReader.Read(stage).Mods.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
            if (!updated.TryGetValue(target.Id, out var item) || !StringComparer.OrdinalIgnoreCase.Equals(item.Category, category))
                throw new DomainException("Metadata.VerificationFailed", "Requested classification was not retained", "inspect the stage metadata", "InvalidResult", target.Id);
    }

    public static void Enable(ProjectContext stage, ImmutableArray<string> requested, int position)
    {
        var snapshot = InventoryReader.Read(stage);
        _ = DefaultOptionsDetector.AssertInstalled(DefaultOptionsDetector.Read(snapshot), stage.Id);
        var resources = snapshot.Resources.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var selectors = requested.Select(id => resources.TryGetValue(id, out var item) && item.Filename is { } filename ? filename
            : throw new DomainException("ResourcePack.NotFound", "Requested resource has no selectable filename", "inspect the stage inventory", "ObjectNotFound", id)).ToImmutableArray();
        var targets = selectors.IsEmpty ? [] : ResourcePackSelector.Resolve(snapshot, selectors);
        var selected = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var ids = targets.Where(target => selected.Add(target.DefaultId)).Select(target => target.DefaultId).ToArray();
        var ordered = snapshot.ResourceView.Active.Where(item => !selected.Contains(item.Id)).Select(item => item.Id).ToList();
        if (position < 1 || position > ordered.Count + 1)
            throw new DomainException("ResourcePack.InvalidPosition", $"Block position must be between 1 and {ordered.Count + 1}",
                "choose a position in the list after removing the selected packs");
        ordered.InsertRange(position - 1, ids);
        ResourceOrderEditor.Write(stage, ordered, snapshot.ResourceView.Active.Select(item => item.Id));
    }

    private static void Write(ProjectContext stage, string relative, string text)
    {
        var path = TransactionFiles.Within(stage.Root, relative); var encoding = new UTF8Encoding(false, true); _ = encoding.GetByteCount(text);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, encoding);
    }
}
