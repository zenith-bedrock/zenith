using Zenith.ProtocolImport.Schema;
using Xunit;

namespace Zenith.ProtocolImport.Tests;

public sealed class SchemaCacheTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "protocol-import-cache-" + Guid.NewGuid());

    [Fact]
    public async Task Publish_is_deterministic_for_the_same_resolved_source()
    {
        var first = await PublishAsync("abc123", "same schema");
        var second = await PublishAsync("abc123", "same schema");

        Assert.Equal(first.ResolvedSha, second.ResolvedSha);
        Assert.Equal(first.Files, second.Files);
        Assert.Empty(SchemaCache.Validate(_cacheDir, "fake"));
        Assert.Equal(Path.Combine(_cacheDir, "fake", "snapshots", "abc123"), SchemaCache.GetReadRoot(_cacheDir, "fake"));
    }

    [Fact]
    public async Task Validate_detects_a_corrupted_active_snapshot()
    {
        await PublishAsync("def456", "original");
        var file = Path.Combine(SchemaCache.GetReadRoot(_cacheDir, "fake"), "packet.json");
        await File.WriteAllTextAsync(file, "changed");

        Assert.Contains(SchemaCache.Validate(_cacheDir, "fake"), error => error.StartsWith("Hash mismatch:", StringComparison.Ordinal));
        Assert.Contains(SchemaCache.ValidateSnapshot(SchemaCache.GetReadRoot(_cacheDir, "fake")), error => error.StartsWith("Hash mismatch:", StringComparison.Ordinal));
    }

    private Task<CacheManifest> PublishAsync(string sha, string contents) => SchemaCache.PublishAsync(
        _cacheDir, "fake", "stable", sha,
        async (staging, ct) => await File.WriteAllTextAsync(Path.Combine(staging, "packet.json"), contents, ct),
        CancellationToken.None);

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }
}
