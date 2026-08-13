using Zenith.ProtocolImport.Schema;
using Xunit;

namespace Zenith.ProtocolImport.Tests;

public sealed class LocalGitSchemaRepositoryTests
{
    [Fact]
    public async Task Local_repository_resolves_refs_and_reads_objects_without_checkout()
    {
        var git = new FakeGitRunner(
            new[] { "rev-parse", "--is-inside-work-tree" }, "true\n",
            new[] { "rev-parse", "--verify", "r26_u4^{commit}" }, "abc123\n",
            new[] { "ls-tree", "-r", "--name-only", "abc123", "--", "packets" }, "packets/AnimatePacket.json\npackets/nested/Ignore.json\n",
            new[] { "show", "abc123:packets/AnimatePacket.json" }, "{\"name\":\"AnimatePacket\"}");
        using var repository = new LocalGitSchemaRepository(Path.GetTempPath(), git);

        var sha = await repository.ResolveCommitShaAsync("r26_u4", CancellationToken.None);
        var files = await repository.ListFilesAsync("packets", sha, CancellationToken.None);
        var content = await repository.ReadFileAsync("packets/AnimatePacket.json", sha, CancellationToken.None);

        Assert.Equal("abc123", sha);
        Assert.Equal(["AnimatePacket.json"], files.Select(file => file.Name));
        Assert.Equal("{\"name\":\"AnimatePacket\"}", content);
        Assert.DoesNotContain(git.Commands, command => command.Contains("checkout", StringComparison.Ordinal));
    }

    private sealed class FakeGitRunner(params object[] entries) : IGitCommandRunner
    {
        private readonly Dictionary<string, string> _responses = entries
            .Chunk(2)
            .ToDictionary(pair => string.Join('\u001f', (IReadOnlyList<string>)pair[0]), pair => (string)pair[1]);

        public List<string> Commands { get; } = [];

        public string Run(string repository, IReadOnlyList<string> arguments)
        {
            var key = string.Join('\u001f', arguments);
            Commands.Add(string.Join(' ', arguments));
            return _responses.TryGetValue(key, out var value)
                ? value
                : throw new Xunit.Sdk.XunitException($"Unexpected Git command: {string.Join(' ', arguments)}");
        }
    }
}
