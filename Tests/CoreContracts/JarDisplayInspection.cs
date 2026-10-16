using System.Globalization;
using System.Text.Json;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Inventory;

internal static class JarDisplayInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || arguments.Length % 2 != 0)
                throw new ArgumentException("Use --inspect-jar-display <path> [--max-entry-bytes n] [--max-central-bytes n] [--max-entries n].");
            var limits = new ArchiveReadLimits();
            for (var index = 2; index < arguments.Length; index += 2)
                limits = arguments[index] switch
                {
                    "--max-entry-bytes" => limits with { MaximumMetadataBytes = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    "--max-central-bytes" => limits with { MaximumCentralDirectoryBytes = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    "--max-entries" => limits with { MaximumEntries = int.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    _ => throw new ArgumentException("Unknown archive inspection option.")
                };
            var result = JarDisplayReader.Read(arguments[1], limits);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartObject();
            writer.WriteString("name", result.Name);
            writer.WriteString("side", result.Side);
            writer.WriteString("status", result.Status.ToString().ToLowerInvariant());
            writer.WriteString("diagnostic", result.Diagnostic);
            writer.WriteEndObject();
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null,
                "check archive inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
