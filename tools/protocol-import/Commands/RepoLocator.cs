namespace Zenith.ProtocolImport.Commands;

/// <summary>
/// Finds the zenith repo root by walking up from the current working directory looking for
/// zenith.sln, so defaults like "where is src/zenith/Packets" work regardless of whether the
/// tool is invoked from the repo root, from tools/protocol-import, or via `dotnet run --project`
/// from somewhere else entirely - a relative "../../src/zenith/Packets" default only worked
/// from one specific invocation directory.
/// </summary>
internal static class RepoLocator
{
    private const int MaxHops = 8;

    public static string? FindRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        for (var i = 0; i < MaxHops && dir is not null; i++, dir = dir.Parent)
        {
            if (dir.GetFiles("zenith.sln").Length > 0)
                return dir.FullName;
        }

        return null;
    }
}
