using System.Globalization;
using System.Numerics;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class EditorialInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 2) throw new ArgumentException("Use --inspect-editorial <root>.");
            var result = EditorialMetadataReader.Read(ProjectReader.Read(arguments[1]));
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes)) WriteData(writer, result.Data);
            bytes.Position = 0;
            bytes.CopyTo(Console.OpenStandardOutput());
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
                "check editorial inspection arguments and input access permissions", null);
            return 2;
        }
    }

    private static void WriteData(Utf8JsonWriter writer, PsdValue value)
    {
        switch (value)
        {
            case PsdTable table:
                writer.WriteStartObject();
                foreach (var (key, child) in table.Entries) { writer.WritePropertyName(key); WriteData(writer, child); }
                writer.WriteEndObject();
                break;
            case PsdArray array:
                writer.WriteStartArray();
                foreach (var child in array.Items) WriteData(writer, child);
                writer.WriteEndArray();
                break;
            case PsdString text: writer.WriteStringValue(text.Value); break;
            case PsdBoolean flag: writer.WriteBooleanValue(flag.Value); break;
            case PsdNull: writer.WriteNullValue(); break;
            case PsdNumber number:
                if (number.Literal.IndexOfAny(['.', 'e', 'E']) < 0 && long.TryParse(number.Literal, CultureInfo.InvariantCulture, out var integer))
                    writer.WriteNumberValue(integer);
                else if (number.Literal.IndexOfAny(['.', 'e', 'E']) < 0 && decimal.TryParse(number.Literal, CultureInfo.InvariantCulture, out var decimalInteger))
                    writer.WriteRawValue(decimalInteger.ToString(CultureInfo.InvariantCulture) + ".0");
                else
                {
                    // PowerShell converts overflowing integer tokens through BigInteger,
                    // whose double conversion differs from parsing a floating token.
                    var floating = number.Literal.IndexOfAny(['.', 'e', 'E']) < 0
                        ? (double)BigInteger.Parse(number.Literal, CultureInfo.InvariantCulture)
                        : double.Parse(number.Literal, CultureInfo.InvariantCulture);
                    var formatted = floating.ToString("R", CultureInfo.InvariantCulture);
                    if (!double.IsFinite(floating)) writer.WriteStringValue(formatted);
                    else writer.WriteRawValue(formatted.IndexOfAny(['.', 'E', 'e']) >= 0 ? formatted : formatted + ".0");
                }
                break;
            default: throw new InvalidOperationException("Unknown data node.");
        }
    }
}
