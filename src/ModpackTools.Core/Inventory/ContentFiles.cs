namespace ModpackTools.Core.Inventory;

internal static class ContentFiles
{
    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = FileAttributes.Hidden,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Win32
    };

    public static FileInfo[] InDirectory(DirectoryInfo directory, string pattern, Action<FileInfo> matched)
    {
        try
        {
            return directory.EnumerateFiles(pattern, Options)
                .Select(file => { matched(file); return file; })
                .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IEnumerable<FileInfo> Enumerate(string path, ReadBudget budget, string pattern = "*.pw.toml")
    {
        if (!Directory.Exists(path)) yield break;
        var pending = new Stack<DirectoryInfo>();
        budget.Directory(path);
        pending.Push(new(path));
        while (pending.TryPop(out var directory))
        {
            var files = InDirectory(directory, pattern, file => budget.File(file.FullName));
            foreach (var file in files) yield return file;
            var children = new List<DirectoryInfo>();
            try
            {
                foreach (var child in directory.EnumerateDirectories("*", Options))
                {
                    // Directory links are skipped; cloud reparse points have no
                    // link target and remain ordinary readable directories.
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0 && child.LinkTarget is not null) continue;
                    budget.Directory(child.FullName);
                    children.Add(child);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                children.Clear();
            }
            for (var index = children.Count - 1; index >= 0; index--) pending.Push(children[index]);
        }
    }
}
