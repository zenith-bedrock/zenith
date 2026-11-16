using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Policy;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Policy over container access: an open is judged before anything is created or sent, and a right withdrawn while a window is
/// open takes effect on the next request (window closed, request refused, contents conserved).
/// </summary>
public sealed class ContainerAccessPolicyTests
{
    private sealed class Throws : IContainerAccessPolicy
    {
        public PolicyDecision Evaluate(in ContainerAccessProposal proposal) => throw new InvalidOperationException("policy bug");
    }

    private static void Deny(IntentTestFixture fx, Player.Player player, string node) =>
        fx.Context.Permissions.Replace(PermissionPolicy.Create(
            fx.Context.Permissions.Registry, [], null,
            new Dictionary<string, List<string>> { [player.Uuid.ToString()] = [node] }, false));

    private static void PlaceChest(IntentTestFixture fx, Player.Player player)
    {
        fx.World.SetBlock(4, 64, 4, Blocks.ChestForFacing(Blocks.CardinalSouth));
        fx.World.Chests.Ensure(4, 64, 4);
        player.PositionX = 4.5f;
        player.PositionY = 64;
        player.PositionZ = 4.5f;
    }

    private static void Open(IntentTestFixture fx, Player.Player player)
    {
        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(4, 64, 4)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
    }

    private static void MoveIntoChest(IntentTestFixture fx, Player.Player player)
    {
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.OpenContainer(0), 2,
                new WireSlot(InventoryContainerMap.Hotbar, 0), new WireSlot(InventoryContainerMap.Chest, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public void An_open_the_player_may_not_make_creates_no_window_no_opener_and_no_lid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("snoop");
        PlaceChest(fx, player);
        Deny(fx, player, PermissionNodes.ContainerOpen);

        Open(fx, player);

        Assert.Null(player.Windows.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(4, 64, 4));
    }

    [Fact]
    public void A_permitted_open_still_works()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("owner");
        PlaceChest(fx, player);

        Open(fx, player);

        Assert.NotNull(player.Windows.OpenChest);
        Assert.Equal(1, fx.World.Chests.OpenerCount(4, 64, 4));
    }

    [Fact]
    public void A_transfer_right_withdrawn_while_the_window_is_open_closes_it_and_conserves_every_item()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("revoked");
        PlaceChest(fx, player);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 5));
        Open(fx, player);
        Assert.NotNull(player.Windows.OpenChest);

        Deny(fx, player, PermissionNodes.ContainerTransfer);
        MoveIntoChest(fx, player);

        Assert.Null(player.Windows.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(4, 64, 4));
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.True(fx.World.Chests.Get(4, 64, 4, 0).IsEmpty);
    }

    [Fact]
    public void While_access_holds_a_transfer_moves_items_as_before()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mover");
        PlaceChest(fx, player);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 5));
        Open(fx, player);

        MoveIntoChest(fx, player);

        Assert.Equal(3, player.Inventory.Get(0).Count);
        Assert.Equal(2, fx.World.Chests.Get(4, 64, 4, 0).Count);
    }

    [Fact]
    public void A_policy_that_throws_denies_the_open()
    {
        var fx = new IntentTestFixture();
        fx.Context.ContainerPolicies.Register(new Throws());
        var player = fx.AddInGamePlayer("unlucky");
        PlaceChest(fx, player);

        Open(fx, player);

        Assert.Null(player.Windows.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(4, 64, 4));
    }

    [Fact]
    public void Policies_cannot_be_added_once_composition_has_frozen_them()
    {
        var policies = new ContainerAccessPolicies();
        policies.Freeze();

        Assert.Throws<InvalidOperationException>(() => policies.Register(new Throws()));
    }
}
