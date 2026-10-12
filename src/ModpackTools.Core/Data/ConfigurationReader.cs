namespace ModpackTools.Core.Data;

public static class ConfigurationReader
{
    public static string ReadRoot(string configurationDirectory)
    {
        var path = Path.Combine(configurationDirectory, "config.psd1");
        PsdTable? data = null;
        if (File.Exists(path))
        {
            try { data = PsdReader.Parse(TextFile.Read(path)); }
            catch (FormatException exception)
            {
                throw new DomainException("Configuration.InvalidFile", $"Configuration file '{path}' is not a valid PSD1 file",
                    $"repair or remove '{path}', then run modpack config set root <directory>", "InvalidData", path, details: exception.Message);
            }
        }
        var root = data is not null && data.Entries.TryGetValue("Root", out var value) ? DataScalars.String(value) : "";
        if (string.IsNullOrWhiteSpace(root))
            throw new DomainException("Configuration.MissingRoot", "No modpack root is configured", "modpack config set root <directory>", "ObjectNotFound");
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            throw new DomainException("Configuration.RootNotFound", $"Configured modpack root '{root}' does not exist", "modpack config set root <existing-directory>", "ObjectNotFound", root);
        return root;
    }
}
