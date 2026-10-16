using ModpackTools.Core.Artifacts;

namespace ModpackTools.Core.Graph;

internal sealed class ContentNodeBudget(GraphReportLimits limits)
{
    public int Mods { get; private set; }
    public int Requirements { get; private set; }
    public int Aliases { get; private set; }
    public int Warnings => warnings;
    private int warnings;
    private long characters;
    public void Warning(string value) { GraphResource.Guard(warnings++ >= limits.MaximumWarnings, nameof(limits.MaximumWarnings)); Text(value); }
    public void Requirement()
    { GraphResource.Guard(Requirements >= limits.MaximumRequirements, nameof(limits.MaximumRequirements)); Requirements++; }
    public void Mod(ArtifactMod mod)
    {
        GraphResource.Guard(Mods >= limits.MaximumMods, nameof(limits.MaximumMods)); Mods++;
        Text(mod.Declaration.Id); Text(mod.Declaration.Version); Text(mod.Declaration.Side);
        foreach (var alias in Items(mod.Declaration.Provides))
        { GraphResource.Guard(Aliases >= limits.MaximumAliases, nameof(limits.MaximumAliases)); Aliases++; Text(alias.Key.StringValue ?? alias.Key.OpaqueJson ?? ""); Text(alias.Version); }
        foreach (var requirement in Items(mod.Declaration.Requirements)) CountRequirement(requirement);
    }
    public void Node(GraphNode node) { Text(node.Id); Text(node.Name); Text(node.VersionId); Text(node.Kind); Text(node.Side); }
    public void CountRequirement(ManifestRequirement requirement)
    {
        Requirement(); Text(requirement.Target); Text(requirement.Kind); Text(requirement.Side); Text(requirement.Scope);
        Text(requirement.Source); Text(requirement.SuggestedVersionId); Text(requirement.Constraint.PublicJson);
        Text(requirement.Constraint.LegacyJson); Text(requirement.Constraint.TypedFingerprint); Text(requirement.Constraint.Display);
        foreach (var child in Items(requirement.Any)) CountRequirement(child);
        foreach (var child in Items(requirement.All)) CountRequirement(child);
        if (requirement.Unless is not null) CountRequirement(requirement.Unless);
    }
    private void Text(string value) { GraphResource.Guard(value.Length > limits.MaximumTextCharacters - characters, nameof(limits.MaximumTextCharacters)); characters += value.Length; }
    private static System.Collections.Immutable.ImmutableArray<T> Items<T>(System.Collections.Immutable.ImmutableArray<T> values) => values.IsDefault ? [] : values;
}
