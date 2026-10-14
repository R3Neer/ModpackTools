using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

internal static class ConstraintInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || (arguments.Length - 2) % 2 != 0 || arguments[1].Length > 1024 * 1024)
                throw new ArgumentException("Use --inspect-constraint-literal <strict-json> [--member-case-insensitive bool] [--range-probes json-array].");
            var insensitive = false; var probeJson = "[]"; var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 2; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate constraint inspection option.");
                switch (arguments[index])
                {
                    case "--member-case-insensitive": insensitive = bool.Parse(arguments[index + 1]); break;
                    case "--range-probes": probeJson = arguments[index + 1]; break;
                    default: throw new ArgumentException("Unsupported constraint inspection option.");
                }
            }
            if (probeJson.Length > 1024 * 1024) throw new ArgumentException("Constraint probes exceed their input bound.");
            using var probes = JsonDocument.Parse(probeJson, new() { MaxDepth = 16 });
            if (probes.RootElement.GetArrayLength() > 64) throw new ArgumentException("Too many constraint probes.");
            var constraint = VersionConstraint.FromJsonLiteral(arguments[1], insensitive);
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject(); writer.WriteString("LegacyJson", constraint.LegacyJson); writer.WriteString("PublicJson", constraint.PublicJson);
                writer.WriteString("TypedFingerprint", constraint.TypedFingerprint); writer.WriteStartArray("RangeChecks");
                foreach (var probe in probes.RootElement.EnumerateArray())
                {
                    var version = probe.GetString(); writer.WriteStartObject(); writer.WriteString("Version", version);
                    try { var result = VersionRanges.Evaluate(version, constraint.SemanticRange); writer.WritePropertyName("Result"); if (result is { } value) writer.WriteBooleanValue(value); else writer.WriteNullValue(); writer.WriteNull("Error"); }
                    catch (DomainException exception) { writer.WriteNull("Result"); writer.WriteString("Error", exception.Id); }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length)); return 0;
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check constraint inspection JSON and options", null); return 2; }
    }
}
