using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class DefaultOptionsInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || (arguments.Length - 2) % 2 != 0)
                throw new ArgumentException("Use --inspect-default-options <root> [--assert-installed true|false] [--culture name].");
            var required = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 2; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate Default Options inspection option.");
                switch (arguments[index])
                {
                    case "--assert-installed": required = bool.Parse(arguments[index + 1]); break;
                    case "--culture": CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(arguments[index + 1]); break;
                    default: throw new ArgumentException("Unsupported Default Options inspection option: " + arguments[index]);
                }
            }
            var snapshot = InventoryReader.Read(ProjectReader.Read(arguments[1]));
            var status = DefaultOptionsDetector.Read(snapshot);
            if (required) status = DefaultOptionsDetector.AssertInstalled(status, snapshot.Project.Id);
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("Installed", status.Installed);
                writer.WriteBoolean("ConfigPresent", status.ConfigPresent);
                writer.WriteBoolean("Ready", status.Ready);
                writer.WritePropertyName("Mod");
                if (status.Mod is { } mod) InventoryInspection.WritePhysical(writer, mod); else writer.WriteNullValue();
                writer.WriteString("ConfigPath", status.ConfigPath);
                writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or JsonException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null,
                "check Default Options inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
