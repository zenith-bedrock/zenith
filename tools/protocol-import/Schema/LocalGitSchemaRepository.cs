using System.Diagnostics;

namespace Zenith.ProtocolImport.Schema;

/// <summary>
/// Reads schema content from an existing local Git clone. It deliberately never runs checkout:
/// refs are resolved to immutable commits and files are read from the object database, so a pull
/// cannot disturb a developer's working tree or another protocol investigation.
/// </summary>
internal sealed class LocalGitSchemaRepository : ISchemaRepository
{
    private readonly string _repository;
    private readonly IGitCommandRunner _git;

    public LocalGitSchemaRepository(string repository, IGitCommandRunner? git = null)
    {
        if (string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("A local schema repository path is required.", nameof(repository));

        _repository = Path.GetFullPath(repository);
        if (!Directory.Exists(_repository))
            throw new DirectoryNotFoundException($"Local schema repository does not exist: {_repository}");

        _git = git ?? new ProcessGitCommandRunner();
        var isRepository = Run(["rev-parse", "--is-inside-work-tree"]);
        if (!string.Equals(isRepository.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Local schema repository is not a Git work tree: {_repository}", nameof(repository));
    }

    public Task<string> ResolveCommitShaAsync(string @ref, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Run(["rev-parse", "--verify", $"{@ref}^{{commit}}"]).Trim());
    }

    public Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(string folder, string @ref, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFolder = NormalizeRelativePath(folder);
        var output = Run(["ls-tree", "-r", "--name-only", @ref, "--", normalizedFolder]);
        var prefix = normalizedFolder.TrimEnd('/') + "/";
        IReadOnlyList<RepositoryFile> files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..])
            .Where(path => !path.Contains('/'))
            .Select(path => new RepositoryFile(path, "file"))
            .ToList();
        return Task.FromResult(files);
    }

    public Task<string> ReadFileAsync(string path, string @ref, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = NormalizeRelativePath(path);
        return Task.FromResult(Run(["show", $"{@ref}:{normalizedPath}"]));
    }

    public void Dispose() { }

    private string Run(IReadOnlyList<string> arguments) => _git.Run(_repository, arguments);

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Split('/').Any(part => part is "." or ".."))
            throw new ArgumentException($"Schema path must be a repository-relative path: {path}", nameof(path));
        return normalized;
    }
}

internal interface IGitCommandRunner
{
    string Run(string repository, IReadOnlyList<string> arguments);
}

internal sealed class ProcessGitCommandRunner : IGitCommandRunner
{
    public string Run(string repository, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git {string.Join(' ', arguments)} failed in {repository}: {stderr.Trim()}");
        return stdout;
    }
}
