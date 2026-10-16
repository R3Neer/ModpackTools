using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Data;

public sealed record VersionRangeDeclarationLimits(int MaximumNodes = 4096, int MaximumDepth = 64);

public static class VersionRangeDeclaration
{
    public static VersionRange Read(JsonElement value, VersionRangeDeclarationLimits? limits = null, StringComparer? memberComparer = null)
    {
        limits ??= new();
        var invalid = limits.MaximumNodes <= 0 ? nameof(limits.MaximumNodes)
            : limits.MaximumDepth is < 1 or > 64 ? nameof(limits.MaximumDepth) : null;
        if (invalid is not null) throw new DomainException("Version.InvalidRangeDeclarationPolicy", "Range-declaration limits are invalid",
            "use positive node quotas and a declaration depth between 1 and 64", target: invalid);
        // Input resource prelude includes fields that semantic short circuits skip.
        var pending = new Stack<(JsonElement Value, int Depth)>();
        pending.Push((value, 1));
        var count = 0;
        while (pending.TryPop(out var entry))
        {
            if (++count > limits.MaximumNodes) Limit(nameof(limits.MaximumNodes));
            if (entry.Depth > limits.MaximumDepth) Limit(nameof(limits.MaximumDepth));
            if (entry.Value.ValueKind == JsonValueKind.Array)
                foreach (var child in entry.Value.EnumerateArray()) Add(child, entry.Depth + 1);
            else if (entry.Value.ValueKind == JsonValueKind.Object)
                foreach (var field in entry.Value.EnumerateObject()) Add(field.Value, entry.Depth + 1);
        }
        return Lower(value, memberComparer ?? StringComparer.Ordinal);

        void Add(JsonElement child, int depth)
        {
            // Bound queued entries before allocating their storage as well.
            if (count + pending.Count >= limits.MaximumNodes) Limit(nameof(limits.MaximumNodes));
            if (depth > limits.MaximumDepth) Limit(nameof(limits.MaximumDepth));
            pending.Push((child, depth));
        }
    }
    private static VersionRange Lower(JsonElement value, StringComparer comparer)
    {
        if (value.ValueKind == JsonValueKind.Null) return VersionRange.Text(null);
        if (value.ValueKind == JsonValueKind.String) return VersionRange.Text(value.GetString());
        if (value.ValueKind == JsonValueKind.Array) return VersionRange.Any(Children(value, comparer));
        if (value.ValueKind != JsonValueKind.Object) return VersionRange.Unknown;
        var any = Member(value, "any", comparer);
        if (Truthy(any)) return VersionRange.Any(Children(any, comparer));
        var all = Member(value, "all", comparer);
        return Truthy(all) ? VersionRange.All(Children(all, comparer)) : VersionRange.Unknown;
    }
    private static JsonElement Member(JsonElement value, string name, StringComparer comparer)
    {
        JsonElement result = default;
        foreach (var field in value.EnumerateObject())
            if (comparer.Equals(field.Name, name)) result = field.Value;
        // The helper's pipeline assignment unwraps a single emitted property item.
        if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() == 1) result = result[0];
        return result;
    }
    private static ImmutableArray<VersionRange> Children(JsonElement value, StringComparer comparer)
    {
        if (value.ValueKind != JsonValueKind.Array) return [Lower(value, comparer)];
        var result = ImmutableArray.CreateBuilder<VersionRange>(value.GetArrayLength());
        foreach (var child in value.EnumerateArray()) result.Add(Lower(child, comparer));
        return result.MoveToImmutable();
    }
    private static bool Truthy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.String => value.GetString()!.Length != 0,
        JsonValueKind.Number => !value.TryGetDouble(out var number) || number != 0,
        JsonValueKind.Array => value.GetArrayLength() switch { 0 => false, 1 => Truthy(value[0]), _ => true },
        _ => true
    };
    private static void Limit(string property) => throw new DomainException("Version.RangeDeclarationLimitExceeded",
        "Version-range declaration exceeds its resource limits", "reduce the declaration or use explicit quotas within the depth bound",
        "LimitsExceeded", property, details: property + " exceeded.");
}
