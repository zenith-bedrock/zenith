using System.Runtime.CompilerServices;

namespace Zenith.Tests;

/// <summary>
/// Where the repository's files are, for tests that read them. Resolved from this file's own compile-time location, so it holds
/// wherever the build output lands (a custom artifacts path included) and fails loudly instead of skipping when the files are gone.
/// </summary>
static class RepoPaths
{
    public static string Root { get; } = Resolve();

    public static string Source(params string[] parts) => Path.Combine([Root, "src", "zenith", .. parts]);

    private static string Resolve([CallerFilePath] string here = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        if (!File.Exists(Path.Combine(root, "zenith.sln")))
            throw new DirectoryNotFoundException($"zenith.sln not found at {root}; tests read the repository's own files.");
        return root;
    }
}
