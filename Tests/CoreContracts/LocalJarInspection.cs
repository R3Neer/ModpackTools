using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class LocalJarInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || arguments.Length % 2 != 0)
                throw new ArgumentException("Use --inspect-local-jars <root> [--max-files n] [--max-directories n] [--max-bytes n] [--max-entry-bytes n].");
            var limits = new LocalJarReadLimits();
            for (var index = 2; index < arguments.Length; index += 2)
                limits = arguments[index] switch
                {
                    "--max-files" => limits with { MaximumFiles = int.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    "--max-directories" => limits with { MaximumDirectories = int.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    "--max-bytes" => limits with { MaximumMetadataBytes = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    "--max-entry-bytes" => limits with { Archive = new ArchiveReadLimits(MaximumMetadataBytes: long.Parse(arguments[index + 1], CultureInfo.InvariantCulture)) },
                    _ => throw new ArgumentException("Unknown local-JAR inspection option.")
                };
            var project = ProjectReader.Read(arguments[1]);
            var known = ManagedContentReader.Read(project, ContentKind.Mod);
            var items = LocalJarReader.Read(project, known, limits);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartArray();
            foreach (var observation in items)
            {
                var item = observation.Item;
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteString("kind", "mod");
                writer.WriteString("name", item.Name);
                writer.WriteString("filename", item.Filename);
                writer.WriteString("side", item.Side);
                writer.WriteString("source", "local");
                writer.WriteNull("metadata_path");
                writer.WriteNull("category");
                writer.WriteString("status", observation.Status.ToString().ToLowerInvariant());
                writer.WriteString("diagnostic", observation.Diagnostic);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null,
                "check local-JAR inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
