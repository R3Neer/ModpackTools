namespace ModpackTools.Core.Artifacts;

// Invocation-owned emitted budgets; zero is valid here, unlike public policies.
internal sealed record DeclarationAllowance(int Mods, int Diagnostics, bool SuppressMissing)
{
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
