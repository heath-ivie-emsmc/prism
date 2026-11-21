namespace Prism.Tests.Architecture;

/// <summary>Finds the repository on disk so architecture tests can read its source and configuration.</summary>
internal static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    public static IEnumerable<string> Source(string folder, string pattern = "*.cs") =>
        Directory.EnumerateFiles(Path.Combine(Root, folder), pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    public static string Relative(string path) => Path.GetRelativePath(Root, path);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Prism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Prism.slnx was not found above the test output folder.");
    }
}
