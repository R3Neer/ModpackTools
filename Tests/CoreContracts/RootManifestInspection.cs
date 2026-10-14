using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

internal static class RootManifestInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 4 || (arguments.Length - 4) % 2 != 0)
                throw new ArgumentException("Use --inspect-root-manifest <fabric-file-or-null> <quilt-file-or-null> <loader> [--option value].");
            var limits = new RootManifestLimits();
            var probes = ImmutableArray<string?>.Empty;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 4; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate root-manifest inspection option.");
                var value = arguments[index + 1];
                int Number() => int.Parse(value, CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--culture": CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value); break;
                    case "--range-probes": probes = Probes(value); break;
                    case "--max-manifest-chars": limits = limits with { MaximumTextCharacters = Number() }; break;
                    case "--max-manifest-tokens": limits = limits with { MaximumTokenCharacters = Number() }; break;
                    case "--max-manifest-nodes": limits = limits with { MaximumNodes = Number() }; break;
                    case "--max-manifest-depth": limits = limits with { MaximumJsonDepth = Number() }; break;
                    case "--max-requirements": limits = limits with { MaximumRequirements = Number() }; break;
                    case "--max-requirement-depth": limits = limits with { MaximumRequirementDepth = Number() }; break;
                    case "--max-aliases": limits = limits with { MaximumAliases = Number() }; break;
                    case "--max-nested-paths": limits = limits with { MaximumNestedPaths = Number() }; break;
                    default: throw new ArgumentException("Unsupported root-manifest inspection option.");
                }
            }
            var isQuilt = string.Equals(arguments[3], "quilt", StringComparison.InvariantCultureIgnoreCase);
            var result = RootManifestParser.Parse(Read(arguments[1]), isQuilt ? Read(arguments[2]) : null, arguments[3], limits);
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes, new() { MaxDepth = 512 }))
            {
                writer.WriteStartObject();
                writer.WriteString("status", result.Status.ToString());
                writer.WriteBoolean("can_traverse_nested", result.CanTraverseNested);
                writer.WriteStartArray("mods");
                foreach (var mod in result.Mods)
                {
                    writer.WriteStartObject();
                    writer.WriteString("Id", mod.Id); writer.WriteString("Version", mod.Version); writer.WriteString("Side", mod.Side); writer.WriteBoolean("Nested", false);
                    writer.WriteStartArray("Aliases");
                    foreach (var alias in mod.Provides) Alias(writer, alias);
                    writer.WriteEndArray();
                    writer.WriteStartArray("Requirements");
                    foreach (var requirement in mod.Requirements) Requirement(writer, requirement, probes);
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("warnings"); foreach (var diagnostic in result.Diagnostics) writer.WriteStringValue(diagnostic.Message); writer.WriteEndArray();
                writer.WriteStartArray("diagnostics");
                foreach (var diagnostic in result.Diagnostics)
                {
                    writer.WriteStartObject(); writer.WriteString("code", diagnostic.Code); writer.WriteString("message", diagnostic.Message);
                    writer.WriteString("manifest", diagnostic.Manifest); writer.WriteString("details", diagnostic.Details); writer.WriteString("hint", diagnostic.Hint); writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("nested_paths"); foreach (var path in result.NestedPaths) writer.WriteStringValue(path); writer.WriteEndArray();
                writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException
            or OverflowException or InvalidOperationException or JsonException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check root-manifest inspection arguments and files", null);
            return 2;
        }
    }
    private static string? Read(string path)
    {
        if (path == "null") return null;
        using var file = File.OpenRead(path);
        using var bytes = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = file.Read(buffer)) != 0)
        {
            if (count > 8 * 1024 * 1024 - bytes.Length) throw new ArgumentException("Diagnostic manifest file exceeds its byte quota.");
            bytes.Write(buffer, 0, count);
        }
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    private static ImmutableArray<string?> Probes(string json)
    {
        if (json.Length > 1024 * 1024) throw new ArgumentException("Range probes exceed their input quota.");
        using var document = JsonDocument.Parse(json, new() { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 64) throw new ArgumentException("Range probes must be a bounded array.");
        return [.. document.RootElement.EnumerateArray().Select(value => value.ValueKind is JsonValueKind.String or JsonValueKind.Null
            ? value.GetString() : throw new ArgumentException("A range probe must be a string or null."))];
    }
    private static void Alias(Utf8JsonWriter writer, ManifestAlias alias)
    {
        var key = alias.Key;
        writer.WriteStartObject(); writer.WriteString("key_kind", key.Kind.ToString());
        writer.WriteString("key_type", key.Kind switch
        {
            ManifestAliasKeyKind.String => "System.String",
            ManifestAliasKeyKind.Int64 => "System.Int64",
            ManifestAliasKeyKind.Double => "System.Double",
            ManifestAliasKeyKind.BigInteger => "System.Numerics.BigInteger",
            ManifestAliasKeyKind.Boolean => "System.Boolean",
            ManifestAliasKeyKind.Date => "System.DateTime",
            ManifestAliasKeyKind.Array => "System.Object[]",
            _ => "System.Management.Automation.OrderedHashtable"
        });
        writer.WritePropertyName("key");
        switch (key.Kind)
        {
            case ManifestAliasKeyKind.String: writer.WriteStringValue(key.StringValue); break;
            case ManifestAliasKeyKind.Int64: writer.WriteNumberValue(key.IntegerValue!.Value); break;
            case ManifestAliasKeyKind.BigInteger: writer.WriteRawValue(key.BigIntegerValue!.Value.ToString(CultureInfo.InvariantCulture)); break;
            case ManifestAliasKeyKind.Double:
                var number = key.FloatingValue!.Value;
                if (double.IsFinite(number)) writer.WriteNumberValue(number); else writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture)); break;
            case ManifestAliasKeyKind.Boolean: writer.WriteBooleanValue(key.BooleanValue!.Value); break;
            case ManifestAliasKeyKind.Date: writer.WriteStringValue(key.DateValue!.Value); break;
            default: writer.WriteRawValue(key.OpaqueJson!); break;
        }
        writer.WriteString("value", alias.Version); writer.WriteEndObject();
    }
    private static void Requirement(Utf8JsonWriter writer, ManifestRequirement requirement, ImmutableArray<string?> probes)
    {
        writer.WriteStartObject(); writer.WriteString("Target", requirement.Target); writer.WriteString("RangeDisplay", requirement.RangeDisplay);
        writer.WriteString("Kind", requirement.Kind); writer.WriteString("Side", requirement.Side); writer.WriteString("Scope", requirement.Scope);
        writer.WriteBoolean("Maven", requirement.Scheme == VersionScheme.Maven); writer.WriteString("Source", requirement.Source);
        writer.WriteString("SuggestedVersionId", requirement.SuggestedVersionId);
        writer.WriteStartArray("RangeChecks");
        foreach (var version in probes)
        {
            writer.WriteStartObject(); writer.WriteString("Version", version);
            try
            {
                var result = VersionRanges.Evaluate(version, requirement.Range, requirement.Scheme);
                writer.WritePropertyName("Result"); if (result is { } matches) writer.WriteBooleanValue(matches); else writer.WriteNullValue();
                writer.WriteNull("Error");
            }
            catch (DomainException exception) { writer.WriteNull("Result"); writer.WriteString("Error", exception.Id); }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("Any"); foreach (var child in requirement.Any) Requirement(writer, child, probes); writer.WriteEndArray();
        writer.WriteStartArray("All"); foreach (var child in requirement.All) Requirement(writer, child, probes); writer.WriteEndArray();
        writer.WritePropertyName("Unless"); if (requirement.Unless is { } unless) Requirement(writer, unless, probes); else writer.WriteNullValue();
        writer.WriteEndObject();
    }
}
