using System.Text.Json;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

internal static class ConstraintEvidenceContracts
{
    public static void Verify()
    {
        VersionConstraint Fabric(string raw) => RootManifestParser.Parse("{schemaVersion:1,id:'m',version:'1',depends:{d:" + raw + "}}", null, "fabric").Mods[0].Requirements[0].Constraint;
        var number = Fabric("NaN"); var text = Fabric("'NaN'");
        if (number.LegacyJson != text.LegacyJson || number.TypedFingerprint == text.TypedFingerprint
            || VersionRanges.Evaluate("NaN", number.SemanticRange) is not null || VersionRanges.Evaluate("NaN", text.SemanticRange) != true)
            throw new InvalidOperationException("Nonfinite constraint evidence lost its semantic type.");
        var left = Fabric("{unknown:1}"); var right = Fabric("{unknown:2}");
        if (left.Display != right.Display || left.LegacyJson == right.LegacyJson || left.TypedFingerprint == right.TypedFingerprint)
            throw new InvalidOperationException("Unknown constraints were identified by display.");
        if (VersionConstraint.Text(null).LegacyJson != "null" || VersionConstraint.Text("").LegacyJson != "\"\""
            || VersionConstraint.Text(null).TypedFingerprint == VersionConstraint.Text("").TypedFingerprint
            || VersionRanges.Evaluate("1", VersionConstraint.FromJsonLiteral("[]").SemanticRange) != false)
            throw new InvalidOperationException("Literal constraint null/empty/array ownership changed.");
        var literalDate = VersionConstraint.FromJsonLiteral("\"2026-10-14T01:02:03Z\"");
        if (VersionRanges.Evaluate("2026-10-14T01:02:03Z", literalDate.SemanticRange) != true
            || literalDate.TypedFingerprint == Fabric("'2026-10-14T01:02:03Z'").TypedFingerprint)
            throw new InvalidOperationException("Literal and metadata date profiles were merged.");
        try { VersionConstraint.FromJsonLiteral("{x:1}"); throw new InvalidOperationException("Literal profile accepted metadata JSON dialect."); }
        catch (JsonException) { }
        var ordinal = VersionConstraint.FromJsonLiteral("{\"ANY\":\"*\"}");
        var insensitive = VersionConstraint.FromJsonLiteral("{\"ANY\":\"*\"}", true);
        if (ordinal.LegacyJson != insensitive.LegacyJson || ordinal.TypedFingerprint == insensitive.TypedFingerprint
            || VersionRanges.Evaluate("1", ordinal.SemanticRange) is not null || VersionRanges.Evaluate("1", insensitive.SemanticRange) != true)
            throw new InvalidOperationException("Lowering profiles were omitted from constraint identity.");
        const string globals = "modLoader='javafml'\nloaderVersion='[1,3)'\n[[mods]]\nmodId='a'\nversion='1'\n[[mods]]\nmodId='b'\nversion='1'";
        var global = ForgeRootManifestParser.Parse(new(globals), "forge");
        var first = global.Mods[0].Requirements[0].Constraint;
        var exact = ForgeRootManifestParser.Parse(new(globals), "forge", new() { Declarations = new(MaximumAggregateConstraintWireCharacters: first.LegacyJson.Length) });
        if (exact.Mods.Length != 2 || !ReferenceEquals(exact.Mods[0].Requirements[0].Constraint, exact.Mods[1].Requirements[0].Constraint)
            || !ReferenceEquals(first.LegacyJson, first.PublicJson)) throw new InvalidOperationException("Shared constraints were copied or charged repeatedly.");
        const string dateToml = "[[mods]]\nmodId='a'\nversion='1'\n[[mods]]\nmodId='b'\nversion='1'\n[[dependencies.b]]\nmodId='d'\nversionRange=2026-10-14T01:02:03.1234567+02:30";
        var date = ForgeRootManifestParser.Parse(new(dateToml), "forge").Mods[1].Requirements[0].Constraint;
        using var legacy = JsonDocument.Parse(date.LegacyJson); using var publicJson = JsonDocument.Parse(date.PublicJson);
        if (legacy.RootElement.GetProperty("Kind").GetInt32() != 1 || legacy.RootElement.GetProperty("SecondPrecision").GetInt32() != 7
            || publicJson.RootElement.GetProperty("Kind").GetString() != "OffsetDateTimeByNumber"
            || !legacy.RootElement.GetProperty("DateTime").GetString()!.EndsWith("+02:30", StringComparison.Ordinal))
            throw new InvalidOperationException("TOML date backing fields or wire profiles were lost.");
        var limited = ForgeRootManifestParser.Parse(new(dateToml), "forge", new() { Declarations = new(MaximumConstraintWireCharacters: 16) });
        if (limited.Mods.Length != 1 || limited.CanTraverseNested || limited.Diagnostics is not [{ Code: "DeclarationLimit" }])
            throw new InvalidOperationException("Evidence quota erased earlier root declarations.");
    }
}
