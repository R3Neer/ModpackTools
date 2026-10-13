using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;

internal static class UpdateTargetInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length is not (3 or 5) || arguments.Length == 5 && arguments[3] != "--type")
                throw new ArgumentException("Use --inspect-update-targets <root> <selectors-json-array> [--type value].");
            ImmutableArray<string> selectors;
            using (var parsed = JsonDocument.Parse(arguments[2], new JsonDocumentOptions { MaxDepth = 16 }))
            {
                var array = parsed.RootElement;
                if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 1024)
                    throw new ArgumentException("Supply between one and 1024 selectors.");
                var values = ImmutableArray.CreateBuilder<string>(array.GetArrayLength());
                foreach (var value in array.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
                        throw new ArgumentException("Selectors must be nonempty strings.");
                    values.Add(value.GetString()!);
                }
                selectors = values.MoveToImmutable();
            }
            var snapshot = InventoryReader.Read(ProjectReader.Read(arguments[1]));
            var items = UpdateTargetSelector.Resolve(snapshot, selectors, arguments.Length == 5 ? arguments[4] : "all");
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartArray();
                foreach (var item in items) InventoryInspection.WritePhysical(writer, item);
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
                "check update-target inspection arguments and input access permissions", null);
            return 2;
        }
    }
}
