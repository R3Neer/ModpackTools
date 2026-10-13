using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Source;

internal static class SourceInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            var changes = arguments[0] == "--inspect-source-changes";
            var text = arguments[0] == "--inspect-text-hash";
            var required = changes ? 3 : 2;
            if (arguments.Length < required || (arguments.Length - required) % 2 != 0 || text && arguments.Length != 2)
                throw new ArgumentException("Use --inspect-source-tree <root>, --inspect-source-changes <before-json-object> <after-json-object>, or --inspect-text-hash <utf16-units-json-array>.");
            var limits = new SourceTreeReadLimits();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = required; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate source inspection option.");
                var value = arguments[index + 1];
                switch (arguments[index])
                {
                    case "--culture": CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value); break;
                    case "--max-tree-files" when !changes: limits = limits with { MaximumFiles = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-tree-directories" when !changes: limits = limits with { MaximumDirectories = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-tree-bytes" when !changes: limits = limits with { MaximumBytes = long.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-tree-path-chars" when !changes: limits = limits with { MaximumPathCharacters = long.Parse(value, CultureInfo.InvariantCulture) }; break;
                    default: throw new ArgumentException("Unsupported source inspection option.");
                }
            }
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                if (text)
                {
                    using var units = Parse(arguments[1]);
                    if (units.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("Supply an array of UTF16 units.");
                    var characters = new char[units.RootElement.GetArrayLength()];
                    var index = 0;
                    foreach (var unit in units.RootElement.EnumerateArray())
                    {
                        if (!unit.TryGetInt32(out var value) || value is < 0 or > ushort.MaxValue) throw new ArgumentException("Invalid UTF16 unit.");
                        characters[index++] = (char)value;
                    }
                    writer.WriteStringValue(TextHash.Compute(new string(characters)));
                }
                else if (changes)
                {
                    var result = SourceTreeChanges.Compare(Map(arguments[1]), Map(arguments[2]));
                    writer.WriteStartArray();
                    foreach (var row in result)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("Path", row.Path);
                        writer.WriteString("Before", row.Before);
                        writer.WriteString("After", row.After);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                else
                {
                    var snapshot = SourceTree.Read(arguments[1], limits);
                    writer.WriteStartObject();
                    writer.WriteString("root", snapshot.Root);
                    writer.WriteStartObject("files");
                    foreach (var (path, digest) in snapshot.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal)) writer.WriteString(path, digest);
                    writer.WriteEndObject();
                    writer.WriteString("health_fingerprint", SourceFingerprint.Health(snapshot));
                    writer.WriteString("source_fingerprint", SourceFingerprint.All(snapshot));
                    writer.WriteString("root_hash", TextHash.Compute(snapshot.Root.ToLowerInvariant()));
                    writer.WriteEndObject();
                }
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null,
                "check source inspection arguments, immutable hash maps and UTF16 units", null);
            return 2;
        }
    }

    private static JsonDocument Parse(string text)
    {
        if (text.Length > 1024 * 1024) throw new ArgumentException("Source inspection JSON exceeds its input quota.");
        return JsonDocument.Parse(text, new() { MaxDepth = 16 });
    }
    private static ImmutableDictionary<string, string?> Map(string text)
    {
        using var document = Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Supply an initialized hash object.");
        var values = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in document.RootElement.EnumerateObject())
        {
            if (field.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) || !values.TryAdd(field.Name, field.Value.GetString()))
                throw new ArgumentException("Hash values must be null or strings with unique ordinal-ignore-case keys.");
        }
        return values.ToImmutable();
    }
}
