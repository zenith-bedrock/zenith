using Xunit;
using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Policy;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.Fluids;
using Zenith.Modules.Protect;
using Zenith.Player;
using Zenith.World;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>
/// The player-driven edits that are not plain place/break — using a door, filling and emptying a bucket — pass the same block-edit
/// policies, so a protected region and the permission nodes hold for them too. Each refusal asserts the world and the items.
/// </summary>
public sealed class ProtectInteractionTests
{
    private static readonly BlockStateTable States = Blocks.Catalog.States;

    private static (int Lower, int Upper) Door()
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:wooden_door", out var lower));
        var upper = lower;
        Assert.True(States.TrySet(ref upper, StateKeys.UpperBlock, true));
        return (lower, upper);
    }

    private static void DenyNode(IntentTestFixture fx, Player.Player player, string node) =>
        fx.Context.Permissions.Replace(PermissionPolicy.Create(
            fx.Context.Permissions.Registry, [], null,
            new Dictionary<string, List<string>> { [player.Uuid.ToString()] = [node] }, false));

    private static BlockEditSystem Edits(IntentTestFixture fx, List<BlocksChanged>? facts = null)
    {
        facts ??= [];
        fx.Context.EventBus.Subscribe<BlocksChanged>(facts.Add);
        return new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability, fx.Context.BlockPolicies, fx.Context.EventBus);
    }

    private static Player.Player PlaceDoor(IntentTestFixture fx, string name, bool op = false)
    {
        var (lower, upper) = Door();
        var player = op ? fx.AddOperator(name) : fx.AddInGamePlayer(name);
        StandNear(player, 3, 65, 0);
        fx.World.SetBlock(1, 64, 0, lower);
        fx.World.SetBlock(1, 65, 0, upper);
        return player;
    }

    [Fact]
    public void Using_a_door_with_one_half_protected_leaves_both_halves_closed()
    {
        var fx = new IntentTestFixture();
        var player = PlaceDoor(fx, "visitor");
        Assert.True(fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("vault", 1, 65, 0, 1, 65, 0), out _));
        var facts = new List<BlocksChanged>();

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(1, 64, 0)));
        Edits(fx, facts).Tick(fx.Clock, fx.Players.Online);

        Assert.False(States.Bit(fx.World.GetBlock(1, 64, 0), StateKeys.Open));
        Assert.False(States.Bit(fx.World.GetBlock(1, 65, 0), StateKeys.Open));
        Assert.Empty(facts);
    }

    [Fact]
    public void An_operator_may_use_a_protected_door_and_the_use_is_one_interact_fact_naming_both_halves()
    {
        var fx = new IntentTestFixture();
        var admin = PlaceDoor(fx, "admin", op: true);
        Assert.True(fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("vault", 1, 64, 0, 1, 65, 0), out _));
        var facts = new List<BlocksChanged>();

        Assert.True(admin.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(1, 64, 0)));
        Edits(fx, facts).Tick(fx.Clock, fx.Players.Online);

        Assert.True(States.Bit(fx.World.GetBlock(1, 64, 0), StateKeys.Open));
        var fact = Assert.Single(facts);
        Assert.Equal(BlockEditKind.Interact, fact.Kind);
        Assert.Equal(2, fact.Cells.Length);
    }

    [Fact]
    public void A_door_outside_every_region_still_opens()
    {
        var fx = new IntentTestFixture();
        var player = PlaceDoor(fx, "visitor");
        Assert.True(fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("far", 50, 0, 50, 60, 90, 60), out _));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(1, 64, 0)));
        Edits(fx).Tick(fx.Clock, fx.Players.Online);

        Assert.True(States.Bit(fx.World.GetBlock(1, 64, 0), StateKeys.Open));
    }

    [Fact]
    public void Without_the_interact_permission_a_door_does_not_open()
    {
        var fx = new IntentTestFixture();
        var player = PlaceDoor(fx, "locked-out");
        DenyNode(fx, player, PermissionNodes.WorldInteract);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(1, 64, 0)));
        Edits(fx).Tick(fx.Clock, fx.Players.Online);

        Assert.False(States.Bit(fx.World.GetBlock(1, 64, 0), StateKeys.Open));
        Assert.False(States.Bit(fx.World.GetBlock(1, 65, 0), StateKeys.Open));
    }

    private const int Floor = 199;
    private const int Level = 200;

    private sealed record Scene(IntentTestFixture Fx, Player.Player Player, BucketItems Buckets, BucketSystem System, List<BlocksChanged> Facts);

    private static async Task<Scene> BucketScene(bool op = false)
    {
        Blocks.EnsureLoaded();
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 4; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        var player = op ? fx.AddOperator("bucketeer") : fx.AddInGamePlayer("bucketeer");
        player.PositionX = 8.5f;
        player.PositionY = Level;
        player.PositionZ = 6.5f;
        var buckets = new BucketItems(fx.Context.ItemPalette);
        var facts = new List<BlocksChanged>();
        fx.Context.EventBus.Subscribe<BlocksChanged>(facts.Add);
        return new Scene(fx, player, buckets,
            new BucketSystem(fx.World, fx.Players, buckets, fx.Context.BlockPolicies, fx.Context.EventBus), facts);
    }

    private static void Use(Scene s, int x, int y, int z, byte face)
    {
        var held = s.Player.Inventory.Get(0);
        Assert.True(s.Player.Intents.SubmitBucket(new BucketIntent(x, y, z, face, 0, held.Id)));
        s.System.Tick(s.Fx.Clock, s.Fx.Players.Online);
    }

    [Fact]
    public async Task Pouring_into_a_protected_cell_keeps_the_bucket_full_and_the_cell_dry()
    {
        var s = await BucketScene();
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Water.Item, 1));
        Assert.True(s.Fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("pond", 8, Level, 8, 8, Level, 8), out _));

        Use(s, 8, Floor, 8, face: 1);

        Assert.Equal(World.World.AirRuntimeId, s.Fx.World.GetBlock(8, Level, 8));
        Assert.Equal(s.Buckets.Water.Item, s.Player.Inventory.Get(0).Id);
        Assert.Empty(s.Facts);
    }

    [Fact]
    public async Task Scooping_a_protected_source_leaves_it_in_place_and_the_bucket_empty()
    {
        var s = await BucketScene();
        s.Fx.World.SetBlock(8, Level, 8, Blocks.Water);
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Empty, 1));
        Assert.True(s.Fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("pond", 8, Level, 8, 8, Level, 8), out _));

        Use(s, 8, Level, 8, face: 1);

        Assert.Equal(Blocks.Water, s.Fx.World.GetBlock(8, Level, 8));
        Assert.Equal(s.Buckets.Empty, s.Player.Inventory.Get(0).Id);
        Assert.Empty(s.Facts);
    }

    [Fact]
    public async Task An_operator_may_pour_into_a_protected_cell_and_one_place_fact_is_published()
    {
        var s = await BucketScene(op: true);
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Water.Item, 1));
        Assert.True(s.Fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("pond", 8, Level, 8, 8, Level, 8), out _));

        Use(s, 8, Floor, 8, face: 1);

        Assert.Equal(Blocks.Water, s.Fx.World.GetBlock(8, Level, 8));
        var fact = Assert.Single(s.Facts);
        Assert.Equal(BlockEditKind.Place, fact.Kind);
    }

    [Fact]
    public async Task Without_the_place_permission_a_bucket_cannot_pour()
    {
        var s = await BucketScene();
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Water.Item, 1));
        DenyNode(s.Fx, s.Player, PermissionNodes.WorldPlace);

        Use(s, 8, Floor, 8, face: 1);

        Assert.Equal(World.World.AirRuntimeId, s.Fx.World.GetBlock(8, Level, 8));
        Assert.Equal(s.Buckets.Water.Item, s.Player.Inventory.Get(0).Id);
    }
}
