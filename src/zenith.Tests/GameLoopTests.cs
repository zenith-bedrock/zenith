using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Raknet.Log;
using Xunit;

namespace Zenith.Tests;

public sealed class GameLoopTests
{
    [Fact]
    public async Task System_failure_stops_the_authoritative_tick_loop()
    {
        var logger = new RecordingLogger();
        var loop = new GameLoop(new GameClock(), new PlayerManager(), logger);
        loop.Register(new ThrowingSystem());
        var following = new CountingSystem();
        loop.Register(following);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loop.RunAsync(CancellationToken.None));

        Assert.Equal("expected failure", exception.Message);
        Assert.Equal(0, following.TickCount);
        Assert.Single(logger.Errors);
        Assert.Contains("Fatal game system failure", logger.Errors[0]);
    }

    private sealed class ThrowingSystem : IGameSystem
    {
        public void Tick(GameClock clock, IReadOnlyList<Player.Player> online) =>
            throw new InvalidOperationException("expected failure");
    }

    private sealed class CountingSystem : IGameSystem
    {
        public int TickCount { get; private set; }

        public void Tick(GameClock clock, IReadOnlyList<Player.Player> online) => TickCount++;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) => Errors.Add(message);
    }
}
