using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class LocalZipInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 2 && !(arguments.Length == 4 && arguments[2] == "--max-files"))
                throw new ArgumentException("Use --inspect-local-zips <root> [--max-files n].");
            var limits = arguments.Length == 4 ? new LocalZipReadLimits(int.Parse(arguments[3], CultureInfo.InvariantCulture)) : new();
            var project = ProjectReader.Read(arguments[1]);
            var known = ManagedContentReader.Read(project, ContentKind.ResourcePack)
                .AddRange(ManagedContentReader.Read(project, ContentKind.ShaderPack));
            var items = LocalZipReader.Read(project, known, limits);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteString("kind", item.Kind == ContentKind.ResourcePack ? "resourcepack" : "shaderpack");
                writer.WriteString("name", item.Name);
                writer.WriteString("filename", item.Filename);
                writer.WriteString("side", item.Side);
                writer.WriteString("source", "local");
                writer.WriteNull("metadata_path");
                writer.WriteNull("category");
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
                "check inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
