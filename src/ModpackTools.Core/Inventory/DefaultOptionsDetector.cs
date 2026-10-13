using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace ModpackTools.Core.Inventory;

public sealed record DefaultOptionsState(InventoryItem? Mod, bool ConfigPresent, string ConfigPath)
{
    public bool Installed => Mod is not null;
    public bool Ready => Installed && ConfigPresent;
}

public static class DefaultOptionsDetector
{
    public static InventoryItem? Find(ImmutableArray<InventoryItem> mods)
    {
        if (mods.IsDefault) throw new ArgumentException("Supply an initialized mod inventory.", nameof(mods));
        Regex? filenamePattern = null;
        foreach (var item in mods)
        {
            if (string.Equals(item.Id, "modrinth:WEg59z5b", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, "Default Options", StringComparison.OrdinalIgnoreCase)) return item;
            if (!string.IsNullOrEmpty(item.Filename))
            {
                filenamePattern ??= new("^defaultoptions(?:-|\\.|_).*\\.jar$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
                if (filenamePattern.IsMatch(item.Filename)) return item;
            }
        }
        return null;
    }

    public static DefaultOptionsState Read(RawInventorySnapshot snapshot)
    {
        var mod = Find(snapshot.Mods);
        var path = Path.Combine(snapshot.Project.Root, "config", "defaultoptions-common.toml");
        return new(mod, File.Exists(path), path);
    }

    public static DefaultOptionsState AssertInstalled(DefaultOptionsState status, string projectId)
    {
        if (!status.Installed)
            throw new DomainException("ResourcePack.DefaultOptionsRequired", $"Default Options is not installed in project '{projectId}', so resource pack activation and order cannot be managed",
                $"modpack -p {projectId} content add WEg59z5b", "NotInstalled", projectId);
        return status;
    }
}
