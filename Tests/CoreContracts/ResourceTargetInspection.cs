using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class ResourceTargetInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 3)
                throw new ArgumentException("Use --inspect-resource-targets <root> <selectors-json-array>.");
            ImmutableArray<string> selectors;
            using (var parsed = JsonDocument.Parse(arguments[2], new JsonDocumentOptions { MaxDepth = 16 }))
            {
                var array = parsed.RootElement;
                if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 1024)
                    throw new ArgumentException("Supply between one and 1024 selectors.");
                var values = ImmutableArray.CreateBuilder<string>(array.GetArrayLength());
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString()))
                        throw new ArgumentException("Selectors must be nonempty strings.");
                    values.Add(item.GetString()!);
                }
                selectors = values.MoveToImmutable();
            }
            var result = ResourcePackSelector.Resolve(InventoryReader.Read(ProjectReader.Read(arguments[1])), selectors);
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartArray();
                foreach (var item in result)
                {
                    writer.WriteStartObject();
                    writer.WriteString("DefaultId", item.DefaultId);
                    writer.WriteString("Id", item.Id);
                    writer.WriteString("Name", item.Name);
                    writer.WriteString("Filename", item.Filename);
                    writer.WriteBoolean("Active", item.Active);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
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
                "check resource-target inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
