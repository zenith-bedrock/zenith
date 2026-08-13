using Zenith.Gameplay;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XX, Priority 4 — leaf tests for the pure viewer-membership-diffing primitive extracted
/// after auditing twelve near-identical copies. No mob/entity type involved: relevance and
/// projection are both caller-supplied.
/// </summary>
public sealed class ViewerReconciliationTests
{
    [Fact]
    public void A_newly_relevant_peer_triggers_enter_exactly_once()
    {
        var fx = new IntentTestFixture();
        var peer = fx.AddInGamePlayer("peer");
        var replicated = new HashSet<(long EntityId, long PlayerId)>();
        var enters = 0;

        ViewerReconciliation.Sync(42, fx.Players.Online, replicated, _ => true, _ => enters++, _ => { });
        ViewerReconciliation.Sync(42, fx.Players.Online, replicated, _ => true, _ => enters++, _ => { });

        Assert.Equal(1, enters);
        Assert.Contains((42L, peer.RuntimeId), replicated);
    }

    [Fact]
    public void A_peer_that_stops_being_relevant_triggers_exit_exactly_once()
    {
        var fx = new IntentTestFixture();
        var peer = fx.AddInGamePlayer("peer");
        var replicated = new HashSet<(long EntityId, long PlayerId)>();
        var exits = 0;
        var relevant = true;

        ViewerReconciliation.Sync(42, fx.Players.Online, replicated, _ => relevant, _ => { }, _ => exits++);
        relevant = false;
        ViewerReconciliation.Sync(42, fx.Players.Online, replicated, _ => relevant, _ => { }, _ => exits++);
        ViewerReconciliation.Sync(42, fx.Players.Online, replicated, _ => relevant, _ => { }, _ => exits++);

        Assert.Equal(1, exits);
        Assert.DoesNotContain((42L, peer.RuntimeId), replicated);
    }

    [Fact]
    public void A_peer_that_never_becomes_relevant_triggers_neither_callback()
    {
        var fx = new IntentTestFixture();
        fx.AddInGamePlayer("irrelevant-peer");
        var replicated = new HashSet<(long EntityId, long PlayerId)>();
        var calls = 0;

        ViewerReconciliation.Sync(42, fx.Players.Online, replicated, _ => false, _ => calls++, _ => calls++);

        Assert.Equal(0, calls);
        Assert.Empty(replicated);
    }

    [Fact]
    public void Different_entity_ids_do_not_share_membership()
    {
        var fx = new IntentTestFixture();
        var peer = fx.AddInGamePlayer("peer");
        var replicated = new HashSet<(long EntityId, long PlayerId)>();

        ViewerReconciliation.Sync(1, fx.Players.Online, replicated, _ => true, _ => { }, _ => { });
        ViewerReconciliation.Sync(2, fx.Players.Online, replicated, _ => true, _ => { }, _ => { });

        Assert.Contains((1L, peer.RuntimeId), replicated);
        Assert.Contains((2L, peer.RuntimeId), replicated);
        Assert.Equal(2, replicated.Count);
    }
}
