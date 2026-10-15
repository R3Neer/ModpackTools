using System.Text.Json;

namespace ModpackTools.Cli;

internal sealed class ProtocolException(string message) : Exception(message);
internal sealed record InvocationContext(string? ActiveProject);
internal sealed record NativeRequest(string[] Arguments, InvocationContext? Context, string? ReviewToken);

internal static class RequestReader
{
    private const int MaximumBytes = 1024 * 1024;

    public static NativeRequest Read(string path)
    {
        try
        {
            return ReadValidated(path);
        }
        catch (InvalidOperationException)
        {
            // JsonDocument accepts escaped lone surrogates but decoding strings
            // rejects them. This is a request error, not a host crash.
            throw new ProtocolException("Request contains an invalid Unicode string.");
        }
    }

    private static NativeRequest ReadValidated(string path)
    {
        using var file = File.OpenRead(path);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = file.Read(chunk)) != 0)
        {
            if (buffer.Length + count > MaximumBytes)
                throw new ProtocolException("Request exceeds the 1 MiB limit.");
            buffer.Write(chunk, 0, count);
        }

        ReadOnlyMemory<byte> bytes = buffer.ToArray();
        if (bytes.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            bytes = bytes[3..];
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProtocolException("Request must be a JSON object.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new ProtocolException($"Duplicate request field '{property.Name}'.");
            if (property.Name is not ("schema_version" or "arguments" or "context" or "review_token"))
                throw new ProtocolException($"Unknown request field '{property.Name}'.");
        }

        if (!root.TryGetProperty("schema_version", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 1)
            throw new ProtocolException("Request schema_version must be the integer 1.");
        if (!root.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Array)
            throw new ProtocolException("Request arguments must be an array of strings.");
        if (arguments.GetArrayLength() > 1024)
            throw new ProtocolException("Request exceeds the 1024 argument limit.");

        var result = new string[arguments.GetArrayLength()];
        var index = 0;
        foreach (var argument in arguments.EnumerateArray())
        {
            if (argument.ValueKind != JsonValueKind.String)
                throw new ProtocolException("Every request argument must be a string.");
            var value = argument.GetString()!;
            if (value.Contains('\0'))
                throw new ProtocolException("Request arguments cannot contain NUL.");
            result[index++] = value;
        }
        InvocationContext? context = null;
        if (root.TryGetProperty("context", out var snapshot))
        {
            if (snapshot.ValueKind != JsonValueKind.Object)
                throw new ProtocolException("Request context must be an object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in snapshot.EnumerateObject())
            {
                if (!fields.Add(property.Name))
                    throw new ProtocolException($"Duplicate context field '{property.Name}'.");
                if (property.Name != "active_project")
                    throw new ProtocolException($"Unknown context field '{property.Name}'.");
            }
            if (!snapshot.TryGetProperty("active_project", out var active) ||
                active.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new ProtocolException("Context active_project must be a string or null.");
            var activeProject = active.GetString();
            if (activeProject?.Contains('\0') == true)
                throw new ProtocolException("Context active_project cannot contain NUL.");
            context = new(activeProject);
        }
        string? reviewToken = null;
        if (root.TryGetProperty("review_token", out var review))
        {
            if (review.ValueKind != JsonValueKind.String || review.GetString() is not { Length: 64 } text ||
                text.Any(character => !char.IsAsciiHexDigit(character)))
                throw new ProtocolException("Request review_token must be exactly 64 hexadecimal characters.");
            reviewToken = text;
        }
        return new(result, context, reviewToken);
    }
}
