namespace ModpackTools.Core.Artifacts;

// Invocation-owned emitted budgets; zero is valid here, unlike public policies.
internal sealed record DeclarationAllowance(int Mods, int Diagnostics, bool SuppressMissing, DeclarationWorkBudget? Work = null)
{
    public void Requirement() => Work?.Requirement();
    public void Alias() => Work?.Alias();
    public void Mod(int constructed) { if (constructed >= Mods) Fail("MaximumMods"); }
    public bool Diagnostic(string code, int constructed)
    {
        if (SuppressMissing && code == "NoSupportedManifest") return false;
        if (constructed >= Diagnostics) Fail("MaximumDiagnostics");
        return true;
    }
    private static void Fail(string property) => throw new RootArchiveEntryException(new("TraversalLimit",
        "Manifest could not be fully read: traversal resource limit exceeded", "", property + " exceeded.",
        "inspect nested declarations or use explicit traversal quotas"));
}

internal sealed class DeclarationWorkBudget(int maximumRequirements, int maximumAliases)
{
    private int requirements, aliases;
    public void Requirement() { if (requirements >= maximumRequirements) Fail("MaximumRequirements"); requirements++; }
    public void Alias() { if (aliases >= maximumAliases) Fail("MaximumAliases"); aliases++; }
    private static void Fail(string property) => throw new RootArchiveEntryException(new("TraversalLimit",
        "Manifest could not be fully read: traversal resource limit exceeded", "", property + " exceeded.",
        "use explicit bounded declaration quotas"));
}
