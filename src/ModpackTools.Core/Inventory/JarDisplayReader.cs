using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Data;

namespace ModpackTools.Core.Inventory;

public enum ArtifactReadStatus { Read, Missing, Unreadable, Limited }
public sealed record ArtifactDisplayMetadata(string Name, string Side, ArtifactReadStatus Status, string? Diagnostic);

public static class JarDisplayReader
{
    public static ArtifactDisplayMetadata Read(string path, ArchiveReadLimits? limits = null) => Read(path, limits, null);

    internal static ArtifactDisplayMetadata Read(string path, ArchiveReadLimits? limits, Action<int>? bytesRead)
    {
        limits ??= new();
        limits.Validate();
        var name = Path.GetFileNameWithoutExtension(path);
        var side = "unknown";
        try
        {
            using var file = File.OpenRead(path);
            ArchivePreflight.Validate(file, limits);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry("fabric.mod.json");
            if (entry is null) return new(name, side, ArtifactReadStatus.Missing, null);
            if (entry.Length > limits.MaximumMetadataBytes)
                throw new ArchiveLimitException("The declared metadata length exceeds the reader budget.");
            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            var buffer = new byte[65536];
            int count;
            while ((count = stream.Read(buffer)) != 0)
            {
                bytesRead?.Invoke(count);
                if (count > limits.MaximumMetadataBytes - bytes.Length)
                    throw new ArchiveLimitException("The actual decompressed metadata exceeds the reader budget.");
                bytes.Write(buffer, 0, count);
            }
            bytes.Position = 0;
            using var reader = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var document = JsonDocument.Parse(reader.ReadToEnd(), new()
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 1024
            });
            JsonDisplayProjection.Validate(document.RootElement);
            var label = JsonDisplayProjection.Field(document.RootElement, "name");
            if (JsonDisplayProjection.Truthy(label)) name = JsonDisplayProjection.String(label);
            var environment = JsonDisplayProjection.String(JsonDisplayProjection.Field(document.RootElement, "environment"));
            side = environment.ToLowerInvariant() switch { "client" => "client", "server" => "server", "*" or "" => "both", _ => "unknown" };
            return new(name, side, ArtifactReadStatus.Read, null);
        }
        catch (ArchiveLimitException exception)
        {
            return new(name, side, ArtifactReadStatus.Limited, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or FormatException or NotSupportedException or InvalidOperationException)
        {
            return new(name, side, ArtifactReadStatus.Unreadable, "Could not read fabric.mod.json: " + exception.Message);
        }
    }
}
