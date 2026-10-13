using System.Text;

namespace ModpackTools.Core.Data;

internal static class TextFile
{
    public static string Read(string path) => Read(path, out _);

    public static string Read(string path, out long byteCount)
    {
        using var file = File.OpenRead(path);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = file.Read(buffer)) != 0)
        {
            if (bytes.Length + count > 8 * 1024 * 1024)
                throw new FormatException("Data file exceeds the 8 MiB limit.");
            bytes.Write(buffer, 0, count);
        }
        byteCount = bytes.Length;
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
