using System.Net.Http.Json;

namespace Zenith.ProtocolImport.Schema;

/// <summary>
/// Thin GitHub "contents" API + raw-file wrapper shared by EndstoneSchemaSource and
/// MojangSchemaSource - both used to independently construct their own HttpClient (same
/// BaseAddress/UserAgent/Accept headers), their own private GitHubEntry DTO, and their own
/// Dispose(), with zero actual per-source variance in any of that. The real per-source
/// differences (folder layout, $ref chasing, ordinal sorting) live entirely in the two
/// ISchemaSource implementations, which compose this rather than each owning an HttpClient.
/// </summary>
internal sealed class GitHubContentClient : IDisposable
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.github.com/")
    };

    private readonly string _owner;
    private readonly string _repo;

    public GitHubContentClient(string owner, string repo)
    {
        _owner = owner;
        _repo = repo;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("zenith-protocol-import/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
    }

    public async Task<IReadOnlyList<GitHubEntry>> ListFilesAsync(string folder, string @ref, CancellationToken ct)
    {
        var listing = await _http.GetFromJsonAsync<List<GitHubEntry>>(
            $"repos/{_owner}/{_repo}/contents/{folder}?ref={Uri.EscapeDataString(@ref)}", ct);
        return listing ?? [];
    }

    public async Task<string> FetchRawAsync(string folder, string fileName, string @ref, CancellationToken ct)
    {
        var rawUrl = $"https://raw.githubusercontent.com/{_owner}/{_repo}/{@ref}/{folder}/{fileName}";
        return await _http.GetStringAsync(rawUrl, ct);
    }

    public void Dispose() => _http.Dispose();

    public sealed class GitHubEntry
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
    }
}
