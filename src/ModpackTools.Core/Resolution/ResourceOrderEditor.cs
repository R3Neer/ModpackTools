using System.Text;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

internal static class ResourceOrderEditor
{
    public static void Write(ProjectContext stage, IReadOnlyList<string> ordered, IEnumerable<string> previous)
    {
        if (ordered.SequenceEqual(previous, StringComparer.Ordinal)) return;
        var path = TransactionFiles.Within(stage.Root, "config/defaultoptions-common.toml");
        if (!File.Exists(path)) throw new DomainException("ResourcePack.DefaultOptionsNotFound", $"Default Options configuration '{path}' does not exist, so resource pack order cannot be changed",
            "install or restore config/defaultoptions-common.toml", "ObjectNotFound", path);
        var text = TextFile.Read(path);
        var parsed = TomlDeclarationReader.Read(text, new());
        if (parsed.Get("defaultResourcePacks") is not TomlSequence)
            throw new DomainException("ResourcePack.MissingTomlArray", "TOML array 'defaultResourcePacks' does not exist", "repair the Default Options configuration", "InvalidData", "defaultResourcePacks");
        var builder = new StringBuilder("[");
        foreach (var id in ordered.AsEnumerable().Reverse())
        {
            var quoted = TomlStringEdits.Quote(id, 4 * 1024 * 1024);
            if (quoted.Length + 2 > 4 * 1024 * 1024 - builder.Length) throw new DomainException("Content.MaterializationLimit", "Resource order exceeds its output quota", "use a bounded order", "LimitsExceeded");
            if (builder.Length > 1) builder.Append(','); builder.Append(quoted);
        }
        builder.Append(']');
        var output = TomlStringEdits.ApplyLiterals(text, [new("", "defaultResourcePacks", builder.ToString())], 4 * 1024 * 1024);
        File.WriteAllText(path, output, new UTF8Encoding(false, true));
        if (!ordered.SequenceEqual(ResourcePackOrderReader.Read(stage), StringComparer.Ordinal))
            throw new DomainException("ResourcePack.VerificationFailed", "Default Options verification did not match the requested resource pack order",
                "inspect config/defaultoptions-common.toml before retrying", "InvalidResult");
    }

}
