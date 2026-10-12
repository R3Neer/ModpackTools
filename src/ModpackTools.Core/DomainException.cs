namespace ModpackTools.Core;

public sealed class DomainException(string id, string message, string hint,
    string category = "InvalidArgument", string? target = null) : Exception(message)
{
    public string Id { get; } = "ModpackTools." + id;
    public string Hint { get; } = hint;
    public string Category { get; } = category;
    public string? Target { get; } = target;
}
