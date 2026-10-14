namespace JevReview;

public sealed class WorkspacePathGuard
{
    public string Root { get; }

    public WorkspacePathGuard(string workspace)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        RejectLinks(Root);
        if (!Directory.Exists(Root)) throw new ArgumentException("workspace_directory_required");
    }

    public string Resolve(string path)
    {
        var full = Path.GetFullPath(path, Root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(Root) ? Root : Root + Path.DirectorySeparatorChar;
        if (!full.Equals(Root, comparison) && !full.StartsWith(prefix, comparison))
            throw new ArgumentException("path_outside_workspace");

        // Check ancestors too, and repeat at each operation in case a root was replaced.
        RejectLinks(full);
        if (!Directory.Exists(Root)) throw new ArgumentException("workspace_directory_required");
        return full;
    }

    private static void RejectLinks(string path)
    {
        for (string? current = path; current != null; current = Directory.GetParent(current)?.FullName)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("linked_path_not_allowed");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
