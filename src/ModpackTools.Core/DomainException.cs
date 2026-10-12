namespace ModpackTools.Core;

public sealed class DomainException(string id, string message, string hint,
    string category = "InvalidArgument", string? target = null, DiagnosticTarget? targetData = null, string? details = null) : Exception(message)
{
    public string Id { get; } = "ModpackTools." + id;
    public string Hint { get; } = hint;
    public string Category { get; } = category;
    public DiagnosticTarget? Target { get; } = targetData ?? (target is null ? null : new TextTarget(target));
    public string? Details { get; } = details;
}
