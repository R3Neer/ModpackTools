using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;

internal static class CompatibleVersionsInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 6 || (arguments.Length - 6) % 2 != 0)
                throw new ArgumentException("Use --inspect-compatible-versions <project> <config> <selector> <now> <base-uri> [--option value].");
            var requests = new ProviderRequestLimits(); var cache = new ReferenceCacheWriteLimits();
            using var cancellation = new CancellationTokenSource();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 6; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate version inspection option.");
                var number = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--max-listing-wire-bytes": requests = requests with { MaximumListingWireBytes = number }; break;
                    case "--max-cache-bytes": cache = new(number); break;
                    case "--cancel-after-ms": if (number == 0) cancellation.Cancel(); else cancellation.CancelAfter(checked((int)number)); break;
                    default: throw new ArgumentException("Unknown version inspection option.");
                }
            }
            var view = CompatibleVersions.ReadForSelectorAsync(ProjectReader.Read(arguments[1]), arguments[3], arguments[2],
                DateTimeOffset.Parse(arguments[4], CultureInfo.InvariantCulture), requests, cache: cache,
                cancellation: cancellation.Token, baseUri: new(arguments[5])).GetAwaiter().GetResult();
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            Write(writer, view); return 0;
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check version inspection arguments", null); return 2; }
    }

    internal static void Write(Utf8JsonWriter writer, CompatibleVersionView view)
    {
        writer.WriteStartObject(); writer.WriteString("item_name", view.ItemName);
        writer.WriteString("item_type", view.ItemKind switch { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", _ => "shaderpack" });
        writer.WriteStartArray("items");
        foreach (var row in view.Versions)
        {
            writer.WriteStartObject(); writer.WriteNumber("number", row.Number); writer.WriteString("id", row.Candidate.Version.Id);
            writer.WriteString("version", row.Candidate.Version.VersionNumber); writer.WriteString("version_type", row.Candidate.VersionType);
            writer.WriteString("filename", row.Candidate.Filename); writer.WriteString("published", row.Candidate.Version.Published.Text);
            writer.WriteBoolean("installed", row.Installed); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }
}
