using Zenith.Gameplay;
using Xunit;

namespace Zenith.Tests;

public sealed class ActorInterestTests
{
    [Fact]
    public void Includes_knownActorColumn_acceptsOnlyKnownInGamePlayer()
    {
        var fx = new IntentTestFixture();
        var near = fx.AddInGamePlayer("near");
        var far = fx.AddInGamePlayer("far");
        near.Chunks.Radius = 1;
        far.Chunks.Radius = 1;
        near.Chunks.RememberMany([(0, 0)]);
        far.Chunks.RememberMany([(9, 9)]);

        Assert.True(ActorInterest.Includes(near, 1f, 1f));
        Assert.False(ActorInterest.Includes(far, 1f, 1f));
    }

    [Fact]
    public void Includes_unboundedFixtureView_acceptsActorWithoutChunkPublication()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fixture");
        player.Chunks.Radius = -1;

        Assert.True(ActorInterest.Includes(player, 10_000f, 10_000f));
    }
}
