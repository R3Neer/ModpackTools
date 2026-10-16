using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Versions;

internal static class VersionOrderingInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 3 || (arguments.Length - 3) % 2 != 0)
                throw new ArgumentException("Use --inspect-compare-version <left-json-string-or-null> <right-json-string-or-null> [--option value].");
            var scheme = VersionScheme.General;
            var limits = new VersionComparisonLimits();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 3; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate version inspection option.");
                var value = arguments[index + 1];
                switch (arguments[index])
                {
                    case "--maven": scheme = bool.Parse(value) ? VersionScheme.Maven : VersionScheme.General; break;
                    case "--culture": CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value); break;
                    case "--max-version-chars": limits = limits with { MaximumTextCharacters = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-version-digits": limits = limits with { MaximumNumericDigits = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-version-items": limits = limits with { MaximumItems = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    case "--max-version-depth": limits = limits with { MaximumDepth = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                    default: throw new ArgumentException("Unsupported version inspection option.");
                }
            }
            var result = VersionOrdering.Compare(Text(arguments[1]), Text(arguments[2]), scheme, limits);
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                if (result is { } value) writer.WriteNumberValue(value); else writer.WriteNullValue();
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null,
                "check version comparison arguments and JSON string types", null);
            return 2;
        }
    }
    private static string? Text(string json)
    {
        if (json.Length > 1024 * 1024) throw new ArgumentException("Version inspection JSON exceeds its input quota.");
        using var document = JsonDocument.Parse(json, new() { MaxDepth = 16 });
        if (document.RootElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            throw new ArgumentException("A version must be a JSON string or null.");
        return document.RootElement.GetString();
    }
}
