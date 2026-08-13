namespace Zenith.ProtocolImport.Schema;

/// <summary>
/// Immutable content view of a schema repository at a resolved Git ref. Implementations may use
/// GitHub or a local clone, but callers always publish the same verified cache snapshot.
/// </summary>
internal interface ISchemaRepository : IDisposable
{
    Task<string> ResolveCommitShaAsync(string @ref, CancellationToken ct);

    Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(string folder, string @ref, CancellationToken ct);

    Task<string> ReadFileAsync(string path, string @ref, CancellationToken ct);
}

internal sealed record RepositoryFile(string Name, string Type);
