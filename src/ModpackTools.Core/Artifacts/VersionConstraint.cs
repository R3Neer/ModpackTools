using System.Text.Json;
using ModpackTools.Core.Data;
using ModpackTools.Core.Versions;

namespace ModpackTools.Core.Artifacts;

public sealed class VersionConstraint
{
    public VersionRange SemanticRange { get; }
    public string Display { get; }
    public string LegacyJson { get; }
    public string PublicJson { get; }
    public string TypedFingerprint { get; }
    private VersionConstraint(VersionRange range, string display, string legacy, string publicJson, string fingerprint)
    { SemanticRange = range; Display = display; LegacyJson = legacy; PublicJson = publicJson; TypedFingerprint = fingerprint; }
    internal static VersionConstraint Captured(VersionRange range, string display, string legacy, string publicJson, string fingerprint)
        => new(range, display, legacy, publicJson, fingerprint);
    public static VersionConstraint Text(string? value) => new ConstraintScope(new()).Json(value is null ? ManifestNull.Value : new ManifestString(value));
    public static VersionConstraint FromJsonLiteral(string json, bool memberCaseInsensitive = false)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 4 * 1024 * 1024) throw new ArgumentException("Constraint literal exceeds its input bound.", nameof(json));
        using var document = JsonDocument.Parse(json, new() { MaxDepth = 64 });
        var range = VersionRangeDeclaration.Read(document.RootElement, memberComparer: memberCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        // Strict validation precedes the private token reader. The wrapper preserves
        // arrays instead of applying the metadata root-pipeline assignment rule.
        var wrapper = (ManifestMap)OrderedManifestReader.Read("{\"value\":" + json + "}", new(MaximumNodes: 4097, MaximumJsonDepth: 65), parseDates: false);
        wrapper.TryGet("value", out var value);
        return new ConstraintScope(new(), memberCaseInsensitive ? (byte)1 : (byte)0).Json(value, range);
    }
}
