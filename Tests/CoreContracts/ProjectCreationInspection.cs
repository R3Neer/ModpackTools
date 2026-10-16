using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;

internal static class ProjectCreationInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-project-creation <request.json>.");
            using var stream = File.OpenRead(args[1]); if (stream.Length > 4 * 1024 * 1024) throw new ArgumentException("Request exceeds 4 MiB.");
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 32 }); var input = document.RootElement;
            string? Optional(string name) => input.TryGetProperty(name, out var value) ? value.GetString() : null;
            var tool = input.GetProperty("Executable").GetString()!;
            var result = ProjectCreation.ExecuteAsync(input.GetProperty("CatalogRoot").GetString()!,
                new(input.GetProperty("Id").GetString()!, input.GetProperty("Name").GetString()!, input.GetProperty("Minecraft").GetString()!, input.GetProperty("Loader").GetString()!,
                    Optional("LoaderVersion"), Optional("DirectoryName"), Optional("PackVersion") ?? "0.1.0", Optional("DisplayVersion")),
                new(File.Exists(tool), tool, "fixture", null), Environment.ProcessPath!, prepareOutput: input.TryGetProperty("FailOutput", out var fail) && fail.GetBoolean()
                    ? (_, _) => throw new IOException("fixture output preparation fails") : null).GetAwaiter().GetResult();
            using var output = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(output))
            {
                var project = result.Project; writer.WriteStartObject(); writer.WriteStartObject("Project");
                writer.WriteString("Id", project.Id); writer.WriteString("Root", project.Root); writer.WriteString("DisplayName", project.Name);
                writer.WriteString("DescriptorPath", Path.Combine(project.Root, ".modpack", "project.psd1"));
                writer.WriteString("TechnicalName", result.TechnicalName); writer.WriteString("PackVersion", result.PackVersion);
                writer.WriteString("DisplayVersion", project.Version); writer.WriteString("OutputName", project.OutputName);
                writer.WriteString("MinecraftVersion", project.Minecraft); writer.WriteString("Loader", project.Loader); writer.WriteString("LoaderVersion", project.LoaderVersion);
                writer.WriteString("IndexFile", project.IndexFile); writer.WriteString("IndexPath", project.IndexPath); writer.WriteEndObject();
                writer.WriteStartArray("Diagnostics"); foreach (var diagnostic in result.Diagnostics)
                { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Code); writer.WriteString("Path", diagnostic.Path); writer.WriteString("Message", diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check creation fixture input", null); return 2; }
    }
}
