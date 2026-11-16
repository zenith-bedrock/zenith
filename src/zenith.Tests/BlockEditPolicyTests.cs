using Xunit;
using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Policy;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>
/// Policy over player block edits: a veto lands before any effect, judges the whole edit (coupled cells included), fails closed, and an
/// applied edit publishes exactly one fact. Each refusal asserts the authoritative resources, not just the return.
/// </summary>
public sealed class BlockEditPolicyTests
{
    private sealed class Vetoes(Func<IReadOnlyList<BlockCellChange>, bool> deny) : IBlockEditPolicy
    {
        public int Calls { get; private set; }

        public PolicyDecision Evaluate(in BlockEditProposal proposal)
        {
            Calls++;
            return deny(proposal.Cells.ToArray()) ? PolicyDecision.Deny("test veto") : PolicyDecision.Allow;
        }
    }

    private sealed class Throws : IBlockEditPolicy
    {
        public PolicyDecision Evaluate(in BlockEditProposal proposal) => throw new InvalidOperationException("policy bug");
    }

    private sealed class Harness
    {
        public IntentTestFixture Fx { get; } = new();
        public List<BlocksChanged> Facts { get; } = [];
        public BlockEditPolicies Policies { get; }
        public BlockEditSystem System { get; }

        public Harness()
        {
            Policies = PermissionsComposition(Fx);
            Fx.Context.EventBus.Subscribe<BlocksChanged>(Facts.Add);
            System = new BlockEditSystem(Fx.Players, Fx.World, Fx.Context.Durability, Policies, Fx.Context.EventBus);
        }

        private static BlockEditPolicies PermissionsComposition(IntentTestFixture fx) =>
            Zenith.Server.Composition.PermissionsComposition.CreateBlockPolicies(fx.Context.Permissions, fx.Log);

        public void Tick() => System.Tick(Fx.Clock, Fx.Players.Online);
    }

    private static BlockStateTable States => Blocks.Catalog.States;

    private static Player.Player Builder(Harness h, GameMode mode = GameMode.Survival)
    {
        var player = h.Fx.AddInGamePlayer("builder", mode);
        StandForPlace(player, 5, 70, 5);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        return player;
    }

    private static void Place(Player.Player player, int x, int y, int z, int block = 0) =>
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(x, y, z, block == 0 ? Blocks.Stone : block, hotbarSlot: 0)));

    [Fact]
    public void An_allowed_placement_is_applied_and_published_once_as_a_complete_fact()
    {
        var h = new Harness();
        var player = Builder(h);

        Place(player, 5, 70, 5);
        h.Tick();

        Assert.Equal(Blocks.Stone, h.Fx.World.GetBlock(5, 70, 5));
        var fact = Assert.Single(h.Facts);
        Assert.Equal(BlockEditKind.Place, fact.Kind);
        Assert.Equal(player.Uuid, fact.ActorId);
        Assert.Equal(player.RuntimeId, fact.ActorSessionId);
        var cell = Assert.Single(fact.Cells);
        Assert.Equal(new BlockCellChange(5, 70, 5, World.World.AirRuntimeId, Blocks.Stone), cell);
    }

    [Fact]
    public void A_player_without_the_place_permission_changes_nothing_and_loses_nothing()
    {
        var h = new Harness();
        var player = Builder(h);
        var stack = player.Inventory.Get(0);
        h.Fx.Context.Permissions.Replace(PermissionPolicy.Create(
            h.Fx.Context.Permissions.Registry, [], null,
            new Dictionary<string, List<string>> { [player.Uuid.ToString()] = [PermissionNodes.WorldPlace] }, false));

        Place(player, 5, 70, 5);
        h.Tick();

        Assert.Equal(World.World.AirRuntimeId, h.Fx.World.GetBlock(5, 70, 5));
        Assert.Equal(stack, player.Inventory.Get(0));
        Assert.Empty(h.Facts);
    }

    [Fact]
    public void A_permission_revoked_after_the_click_but_before_the_tick_stops_the_placement()
    {
        var h = new Harness();
        var player = Builder(h);
        Place(player, 5, 70, 5);

        h.Fx.Context.Permissions.Replace(PermissionPolicy.Create(
            h.Fx.Context.Permissions.Registry, [], null,
            new Dictionary<string, List<string>> { [player.Uuid.ToString()] = [PermissionNodes.WorldPlace] }, false));
        h.Tick();

        Assert.Equal(World.World.AirRuntimeId, h.Fx.World.GetBlock(5, 70, 5));
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Empty(h.Facts);
    }

    [Fact]
    public void A_denied_break_leaves_the_block_the_drops_and_the_tool_untouched()
    {
        var h = new Harness();
        var player = Builder(h, GameMode.Creative);
        h.Fx.World.SetBlock(5, 70, 5, Blocks.Stone);
        h.Fx.Context.Permissions.Replace(PermissionPolicy.Create(
            h.Fx.Context.Permissions.Registry, [], null,
            new Dictionary<string, List<string>> { [player.Uuid.ToString()] = [PermissionNodes.WorldBreak] }, false));
        var drops = h.Fx.World.FloorDrops.Count;

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, World.World.AirRuntimeId, hotbarSlot: -1)));
        h.Tick();

        Assert.Equal(Blocks.Stone, h.Fx.World.GetBlock(5, 70, 5));
        Assert.Equal(drops, h.Fx.World.FloorDrops.Count);
        Assert.Empty(h.Facts);
    }

    [Fact]
    public void A_policy_that_throws_denies_the_edit()
    {
        var h = new Harness();
        h.Policies.Register(new Throws());
        var player = Builder(h);

        Place(player, 5, 70, 5);
        h.Tick();

        Assert.Equal(World.World.AirRuntimeId, h.Fx.World.GetBlock(5, 70, 5));
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Empty(h.Facts);
    }

    [Fact]
    public void Denying_any_cell_of_a_coupled_placement_refuses_the_whole_placement()
    {
        var h = new Harness();
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:wooden_door", out var door));
        // Veto only the door's second cell (the upper half, one above the clicked cell).
        h.Policies.Register(new Vetoes(cells => cells.Any(cell => cell.Y == 71)));
        var player = Builder(h, GameMode.Creative);
        h.Fx.World.SetBlock(5, 69, 5, Blocks.Stone);

        Place(player, 5, 70, 5, door);
        h.Tick();

        Assert.Equal(World.World.AirRuntimeId, h.Fx.World.GetBlock(5, 70, 5));
        Assert.Equal(World.World.AirRuntimeId, h.Fx.World.GetBlock(5, 71, 5));
        Assert.Empty(h.Facts);
    }

    [Fact]
    public void A_coupled_placement_that_is_allowed_is_one_fact_naming_every_cell()
    {
        var h = new Harness();
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:wooden_door", out var door));
        var player = Builder(h, GameMode.Creative);
        h.Fx.World.SetBlock(5, 69, 5, Blocks.Stone);

        Place(player, 5, 70, 5, door);
        h.Tick();

        var fact = Assert.Single(h.Facts);
        Assert.Equal(2, fact.Cells.Length);
        Assert.Contains(fact.Cells, cell => cell.Y == 70);
        Assert.Contains(fact.Cells, cell => cell.Y == 71);
    }

    [Fact]
    public void Breaking_one_half_of_a_door_is_judged_with_the_other_half_and_denied_as_a_whole()
    {
        var h = new Harness();
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:wooden_door", out var door));
        var upper = door;
        Assert.True(States.TrySet(ref upper, StateKeys.UpperBlock, true));
        var player = Builder(h, GameMode.Creative);
        h.Fx.World.SetBlock(5, 70, 5, door);
        h.Fx.World.SetBlock(5, 71, 5, upper);
        var veto = new Vetoes(cells => cells.Any(cell => cell.Y == 71));
        h.Policies.Register(veto);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, World.World.AirRuntimeId, hotbarSlot: -1)));
        h.Tick();

        Assert.Equal(door, h.Fx.World.GetBlock(5, 70, 5));
        Assert.Equal(upper, h.Fx.World.GetBlock(5, 71, 5));
        Assert.Equal(1, veto.Calls);
        Assert.Empty(h.Facts);
    }

    [Fact]
    public void Policies_cannot_be_added_once_composition_has_frozen_them()
    {
        var policies = new BlockEditPolicies();
        policies.Register(new Vetoes(_ => false));
        policies.Freeze();

        Assert.Throws<InvalidOperationException>(() => policies.Register(new Vetoes(_ => false)));
    }

    [Fact]
    public void Block_edit_facts_and_proposals_expose_values_only()
    {
        foreach (var property in typeof(BlocksChanged).GetProperties().Where(p => p.Name != "EqualityContract"))
        {
            Assert.True(
                property.PropertyType.IsValueType || property.PropertyType == typeof(string),
                $"BlocksChanged.{property.Name} is {property.PropertyType.Name}");
        }
    }
}
