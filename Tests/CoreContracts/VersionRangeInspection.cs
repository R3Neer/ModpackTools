using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Versions;

internal static class VersionRangeInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 3 || (arguments.Length - 3) % 2 != 0)
                throw new ArgumentException("Use --inspect-version-range <version-json-string-or-null> <range-json> [--option value].");
            var scheme = VersionScheme.General;
            var limits = new VersionRangeLimits();
            var declaration = new VersionRangeDeclarationLimits();
            var comparison = new VersionComparisonLimits();
            var memberComparer = StringComparer.Ordinal;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 3; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate range inspection option.");
                var value = arguments[index + 1];
                int Number() => int.Parse(value, CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--maven": scheme = bool.Parse(value) ? VersionScheme.Maven : VersionScheme.General; break;
                    case "--culture": CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value); break;
                    case "--member-case-insensitive": memberComparer = bool.Parse(value) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal; break;
                    case "--max-range-chars": limits = limits with { MaximumTextCharacters = Number() }; break;
                    case "--max-range-nodes": limits = limits with { MaximumNodes = Number() }; break;
                    case "--max-range-comparisons": limits = limits with { MaximumComparisons = Number() }; break;
                    case "--max-range-depth": limits = limits with { MaximumDepth = Number() }; break;
                    case "--max-declaration-nodes": declaration = declaration with { MaximumNodes = Number() }; break;
                    case "--max-declaration-depth": declaration = declaration with { MaximumDepth = Number() }; break;
                    case "--max-version-chars": comparison = comparison with { MaximumTextCharacters = Number() }; break;
                    case "--max-version-digits": comparison = comparison with { MaximumNumericDigits = Number() }; break;
                    case "--max-version-items": comparison = comparison with { MaximumItems = Number() }; break;
                    case "--max-version-depth": comparison = comparison with { MaximumDepth = Number() }; break;
                    default: throw new ArgumentException("Unsupported range inspection option.");
                }
            }
            if (arguments[1].Length > 1024 * 1024 || arguments[2].Length > 1024 * 1024)
                throw new ArgumentException("Range inspection JSON exceeds its input quota.");
            using var version = JsonDocument.Parse(arguments[1], new() { MaxDepth = 16 });
            if (version.RootElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new ArgumentException("A version must be a JSON string or null.");
            using var json = JsonDocument.Parse(arguments[2], new() { MaxDepth = 128 });
            var range = VersionRangeDeclaration.Read(json.RootElement, declaration, memberComparer);
            var result = VersionRanges.Evaluate(version.RootElement.GetString(), range, scheme, limits with { Comparison = comparison });
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                if (result is { } value) writer.WriteBooleanValue(value); else writer.WriteNullValue();
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
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check range inspection arguments and JSON types", null);
            return 2;
        }
    }
}
