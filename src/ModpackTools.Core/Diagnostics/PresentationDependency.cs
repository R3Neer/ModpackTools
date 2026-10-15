using System.Text.Json;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Diagnostics;

public static class PresentationDependency
{
    public const string Revision = "18df65b7eff51bc0c893ec687161654ea378ee7f";
    public static DoctorCheck Read(string applicationDirectory, CancellationToken cancellation = default)
    {
        var path = Path.Combine(applicationDirectory, "presentation", "r3cli", "manifest.json");
        try
        {
            cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(path);
            if (!File.Exists(path)) return new("SYSTEM", "fail", "R3CLI", "Unavailable", "Native presentation manifest is missing: " + path, []);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            WindowsProjectIdentity.AssertStreamBytes(file.SafeFileHandle, 256 * 1024, path);
            if (file.Length > 256 * 1024) throw new FormatException("Presentation manifest exceeds its byte quota.");
            using var document = JsonDocument.Parse(file, new() { MaxDepth = 16 }); var manifest = document.RootElement;
            if (manifest.GetProperty("schema_version").GetInt32() != 1 || manifest.GetProperty("revision").GetString() != Revision) throw new FormatException("Presentation revision is not supported.");
            var version = manifest.GetProperty("version").GetString(); if (string.IsNullOrEmpty(version)) throw new FormatException("Presentation version is missing.");
            var files = manifest.GetProperty("files"); if (files.ValueKind != JsonValueKind.Object) throw new FormatException("Presentation files must be a hash map.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var module = false; var resources = false; var license = false;
            foreach (var entry in files.EnumerateObject())
            {
                cancellation.ThrowIfCancellationRequested(); if (names.Count >= 64 || !names.Add(entry.Name)) throw new FormatException("Presentation file set is invalid.");
                var expected = entry.Value.GetString();
                if (expected is not { Length: 64 } || expected.Any(value => !Uri.IsHexDigit(value))) throw new FormatException("Presentation file hash is invalid.");
                var full = TransactionFiles.Within(Path.GetDirectoryName(path)!, entry.Name);
                if (!StringComparer.OrdinalIgnoreCase.Equals(expected, TransactionFiles.Hash(full, 64 * 1024 * 1024, cancellation))) throw new FormatException("Presentation file is missing or changed: " + entry.Name);
                module |= entry.Name.EndsWith(".nu", StringComparison.OrdinalIgnoreCase); resources |= Path.GetFileName(entry.Name).Equals("resources.json", StringComparison.OrdinalIgnoreCase);
                license |= Path.GetFileName(entry.Name).Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(entry.Name).Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase);
            }
            if (!module || !resources || !license) throw new FormatException("Presentation module, resources and license must all be packaged.");
            return new("SYSTEM", "pass", "R3CLI", version + " (" + Revision + ")", path, []);
        }
        catch (DomainException error) { return new("SYSTEM", "fail", "R3CLI", "Unavailable", error.Message, []); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { return new("SYSTEM", "fail", "R3CLI", "Unavailable", error.Message, []); }
    }
}
