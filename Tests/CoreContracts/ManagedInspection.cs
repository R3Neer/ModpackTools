using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class ManagedInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || arguments.Length % 2 != 0)
                throw new ArgumentException("Use --inspect-managed <root> [--max-bytes n] [--max-files n] [--max-directories n].");
            var limits = new ManagedReadLimits();
            for (var index = 2; index < arguments.Length; index += 2)
                limits = arguments[index] switch
                {
                    "--max-bytes" => limits with { MaximumBytes = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    "--max-files" => limits with { MaximumFiles = int.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    "--max-directories" => limits with { MaximumDirectories = int.Parse(arguments[index + 1], CultureInfo.InvariantCulture) },
                    _ => throw new ArgumentException("Unknown managed-inspection option.")
                };
            var project = ProjectReader.Read(arguments[1]);
            var items = ManagedContentReader.Read(project, limits);
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteString("kind", item.Kind switch { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", ContentKind.ShaderPack => "shaderpack", _ => throw new InvalidOperationException("Unknown kind.") });
                writer.WriteString("name", item.Name);
                writer.WriteString("filename", item.Filename);
                writer.WriteString("side", item.Side);
                writer.WriteString("version_id", item.VersionId);
                writer.WriteString("source", "packwiz");
                writer.WriteString("metadata_path", item.MetadataPath);
                writer.WriteNull("category");
                writer.WriteBoolean("pinned", item.Pinned);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            return 0;
        }
        catch (DomainException exception)
        {
            WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
        {
            WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check inspection arguments and input access permissions", null);
            return 2;
        }
    }

    internal static void WriteFailure(string id, string message, string category, DiagnosticTarget? target, string hint, string? details)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
        writer.WriteStartObject();
        writer.WriteBoolean("ok", false);
        writer.WriteStartObject("error");
        writer.WriteString("id", id);
        writer.WriteString("message", message);
        writer.WriteString("category", category);
        writer.WriteString("hint", hint);
        writer.WriteString("details", details);
        writer.WritePropertyName("target");
        switch (target)
        {
            case null: writer.WriteNullValue(); break;
            case TextTarget text: writer.WriteStringValue(text.Value); break;
            case NumberTarget number: writer.WriteNumberValue(number.Value); break;
            case DecimalTarget number: writer.WriteNumberValue(number.Value); break;
            case FloatingTarget number: writer.WriteNumberValue(number.Value); break;
            case BooleanTarget flag: writer.WriteBooleanValue(flag.Value); break;
            case TextListTarget values:
                writer.WriteStartArray();
                foreach (var value in values.Values) writer.WriteStringValue(value);
                writer.WriteEndArray();
                break;
            default: throw new InvalidOperationException("Unknown diagnostic target.");
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
