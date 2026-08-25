using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Tests;

public class BreakDurationTests
{
    public BreakDurationTests()
    {
        Blocks.EnsureLoaded();
        Tools.EnsureLoaded();
    }

    [Fact]
    public void Empty_hand_matches_historical_table()
    {
        Assert.Equal(0, BreakDuration.BreakTicks(Blocks.Air));
        Assert.Equal(15, BreakDuration.BreakTicks(Blocks.Dirt));
        Assert.Equal(15, BreakDuration.BreakTicks(Blocks.Sand));
        Assert.Equal(18, BreakDuration.BreakTicks(Blocks.GrassBlock));
        Assert.Equal(60, BreakDuration.BreakTicks(Blocks.OakPlanks));
        Assert.Equal(60, BreakDuration.BreakTicks(Blocks.OakLog));
        Assert.Equal(75, BreakDuration.BreakTicks(Blocks.Chest));
        Assert.Equal(150, BreakDuration.BreakTicks(Blocks.Stone));
    }

    /// <summary>
    /// Regression for ADR §137: ground-cover vegetation must have a registered DigProfile — a
    /// missing entry means Survival cannot dig it at all (this table's own documented contract),
    /// which would have made every grass tuft/flower an unbreakable obstacle the moment world-gen
    /// started placing them.
    /// </summary>
    [Fact]
    public void Ground_cover_decoration_is_diggable_by_hand()
    {
        Assert.Equal(3, BreakDuration.BreakTicks(Blocks.ShortGrass));
        Assert.Equal(3, BreakDuration.BreakTicks(Blocks.Dandelion));
        Assert.Equal(3, BreakDuration.BreakTicks(Blocks.Poppy));
    }

    [Fact]
    public void Wooden_shovel_on_dirt_is_faster_than_hand()
    {
        var shovel = Tools.Require("minecraft:wooden_shovel");
        Assert.Equal(8, BreakDuration.BreakTicks(Blocks.Dirt, StackId.FromItem(shovel)));
        Assert.Equal(8, BreakDuration.BreakTicks(Blocks.Sand, StackId.FromItem(shovel)));
    }

    [Fact]
    public void Wooden_pick_on_stone_harvests_faster()
    {
        var pick = Tools.Require("minecraft:wooden_pickaxe");
        Assert.Equal(23, BreakDuration.BreakTicks(Blocks.Stone, StackId.FromItem(pick)));
    }

    [Fact]
    public void Wooden_axe_on_planks_and_chest()
    {
        var axe = Tools.Require("minecraft:wooden_axe");
        Assert.Equal(30, BreakDuration.BreakTicks(Blocks.OakPlanks, StackId.FromItem(axe)));
        Assert.Equal(38, BreakDuration.BreakTicks(Blocks.Chest, StackId.FromItem(axe)));
    }

    [Fact]
    public void Wrong_tool_on_stone_stays_hand_slow()
    {
        var axe = Tools.Require("minecraft:wooden_axe");
        var shovel = Tools.Require("minecraft:wooden_shovel");
        Assert.Equal(150, BreakDuration.BreakTicks(Blocks.Stone, StackId.FromItem(axe)));
        Assert.Equal(150, BreakDuration.BreakTicks(Blocks.Stone, StackId.FromItem(shovel)));
    }

    [Fact]
    public void Pickaxe_on_dirt_same_as_hand()
    {
        var pick = Tools.Require("minecraft:iron_pickaxe");
        Assert.Equal(15, BreakDuration.BreakTicks(Blocks.Dirt, StackId.FromItem(pick)));
    }

    [Fact]
    public void Block_in_hand_same_as_empty()
    {
        Assert.Equal(150, BreakDuration.BreakTicks(Blocks.Stone, StackId.FromBlock(Blocks.Dirt)));
    }

    [Fact]
    public void Wood_pickaxe_cannot_harvest_diamond_ore_but_still_digs_faster_than_hand()
    {
        // Phase XXVI: MinHarvestTier gate. Wood pickaxe is kind-effective (still faster than hand),
        // but under diamond ore's Iron minimum tier — no drop.
        var wood = StackId.FromItem(Tools.Require("minecraft:wooden_pickaxe"));
        DigProfiles.TryGet(Blocks.DiamondOre, out var profile);
        var woodTool = Tools.AsTool(Tools.Require("minecraft:wooden_pickaxe"));
        Assert.False(BreakDuration.IsHarvestable(profile, woodTool));

        var handTicks = BreakDuration.BreakTicks(Blocks.DiamondOre);
        var woodTicks = BreakDuration.BreakTicks(Blocks.DiamondOre, wood);
        Assert.True(woodTicks < handTicks);
    }

    [Fact]
    public void Iron_pickaxe_harvests_diamond_ore()
    {
        var iron = Tools.AsTool(Tools.Require("minecraft:iron_pickaxe"));
        DigProfiles.TryGet(Blocks.DiamondOre, out var profile);
        Assert.True(BreakDuration.IsHarvestable(profile, iron));
    }

    [Fact]
    public void Stone_pickaxe_harvests_iron_ore_but_not_diamond_ore()
    {
        var stone = Tools.AsTool(Tools.Require("minecraft:stone_pickaxe"));
        DigProfiles.TryGet(Blocks.IronOre, out var ironProfile);
        DigProfiles.TryGet(Blocks.DiamondOre, out var diamondProfile);
        Assert.True(BreakDuration.IsHarvestable(ironProfile, stone));
        Assert.False(BreakDuration.IsHarvestable(diamondProfile, stone));
    }

    [Fact]
    public void Wood_pickaxe_harvests_coal_ore()
    {
        var wood = Tools.AsTool(Tools.Require("minecraft:wooden_pickaxe"));
        DigProfiles.TryGet(Blocks.CoalOre, out var profile);
        Assert.True(BreakDuration.IsHarvestable(profile, wood));
    }

    [Fact]
    public void Unknown_block_without_DigProfile_returns_minus_one()
    {
        // Palette may contain many rids; curated DigProfiles are sparse (ADR §55).
        Assert.False(DigProfiles.TryGet(int.MaxValue, out _));
        Assert.Equal(-1, BreakDuration.BreakTicks(int.MaxValue));
        Assert.Equal(-1, Blocks.BreakTicks(int.MaxValue));
    }

    [Fact]
    public void Tool_network_ids_do_not_collide_with_curated_blocks()
    {
        var blockIds = new HashSet<int>
        {
            Blocks.Air, Blocks.Stone, Blocks.Dirt, Blocks.GrassBlock,
            Blocks.OakPlanks, Blocks.OakLog, Blocks.Sand, Blocks.Gravel, Blocks.Chest,
            Blocks.ChestForFacing(Blocks.CardinalNorth),
            Blocks.ChestForFacing(Blocks.CardinalEast),
            Blocks.ChestForFacing(Blocks.CardinalWest)
        };
        foreach (var id in Tools.AllNetworkIds)
            Assert.False(blockIds.Contains(id), $"tool id {id} collides with curated block");
    }
}

public class ToolWireTests
{
    public ToolWireTests()
    {
        Blocks.EnsureLoaded();
        Tools.EnsureLoaded();
    }

    [Fact]
    public void CreativeCatalog_includes_twelve_tools()
    {
        var catalog = CreativeCatalog.CreateDefault();
        Assert.True(catalog.TryGet(CreativeCatalog.DiamondPickaxe, out var id, out var count));
        Assert.Equal(StackId.FromItem(Tools.Require("minecraft:diamond_pickaxe")), id);
        Assert.Equal(1, count);
        Assert.Equal(8 + 12, catalog.SnapshotEntries().Count);
    }

    [Fact]
    public void CreativeContent_encodes_tool_with_zero_block_runtime()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var catalog = CreativeCatalog.CreateDefault(palette);
        var packet = CreativeContentBuilder.Build(catalog, palette);
        var diamond = packet.Items.Single(i => i.CreativeItemNetworkId == CreativeCatalog.DiamondPickaxe);
        Assert.Equal(palette.Require("minecraft:diamond_pickaxe"), diamond.Item.NetworkId);
        Assert.Equal(0, diamond.Item.BlockRuntimeId);
    }

    [Fact]
    public void Contrib_DigProfiles_Register_enables_BreakTicks_dx_recipe()
    {
        // DX recipe: DigProfiles.Register (scoped via OverrideForTests) + BreakTicks (ADR §55 / dx.md).
        const int contribRid = 1_999_001;
        Assert.Equal(-1, BreakDuration.BreakTicks(contribRid));

        using (DigProfiles.OverrideForTests(
                   contribRid,
                   destroySpeed: 0.5,
                   harvestTool: ToolKind.None,
                   effectiveTool: ToolKind.Shovel,
                   requiresCorrectTool: false))
        {
            Assert.Equal(15, BreakDuration.BreakTicks(contribRid)); // same DestroySpeed as Dirt
            Assert.Equal(
                8,
                BreakDuration.BreakTicks(
                    contribRid,
                    StackId.FromItem(Tools.Require("minecraft:wooden_shovel"))));
            Assert.Equal(15, BreakDuration.BreakTicks(Blocks.Dirt)); // curated untouched
        }

        Assert.Equal(-1, BreakDuration.BreakTicks(contribRid)); // row removed on dispose
        Assert.Equal(15, BreakDuration.BreakTicks(Blocks.Dirt));
    }
}

public class DigToolIntentTests

{
    public DigToolIntentTests()
    {
        Blocks.EnsureLoaded();
        Tools.EnsureLoaded();
    }

    [Fact]
    public void Dig_start_with_iron_pick_snapshots_faster_need_on_stone()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("miner");
        var pick = Tools.Require("minecraft:iron_pickaxe");
        Assert.True(player.Inventory.TrySetItem(0, pick, 1));
        player.SelectedHotbarSlot = 0;

        var handNeed = Blocks.BreakTicks(Blocks.Stone);
        var pickNeed = Blocks.BreakTicks(Blocks.Stone, StackId.FromItem(pick));
        Assert.True(pickNeed < handNeed);

        Assert.True(player.SubmitDigStart(2, Blocks.FlatSpawnY, 2, startedTick: 10, pickNeed, StackId.FromItem(pick)));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.IsBreakTarget(2, Blocks.FlatSpawnY, 2));
        Assert.Equal(pickNeed, player.BreakRequiredTicks);
        Assert.Equal(StackId.FromItem(pick), player.DigHeldStackId);
    }

    [Fact]
    public void Mid_dig_tool_upgrade_preserves_progress_fraction()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("swapper");
        var wood = Tools.Require("minecraft:wooden_pickaxe");
        var iron = Tools.Require("minecraft:iron_pickaxe");
        Assert.True(player.Inventory.TrySetItem(0, wood, 1));
        Assert.True(player.Inventory.TrySetItem(1, iron, 1));
        player.SelectedHotbarSlot = 0;

        var woodNeed = Blocks.BreakTicks(Blocks.Stone, StackId.FromItem(wood));
        var y = Blocks.FlatSpawnY;
        fx.World.SetBlock(3, y, 3, Blocks.Stone);
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(3, y, 3));
        player.BeginBreak(3, y, 3, tick: fx.Clock.CurrentTick, requiredTicks: woodNeed, heldStackId: StackId.FromItem(wood));

        // Halfway through wood dig.
        fx.Clock.AdvanceBy(woodNeed / 2);
        player.SelectedHotbarSlot = 1;

        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        var ironNeed = Blocks.BreakTicks(Blocks.Stone, StackId.FromItem(iron));
        Assert.Equal(StackId.FromItem(iron), player.DigHeldStackId);
        Assert.Equal(ironNeed, player.BreakRequiredTicks);
        // Started tick adjusted so ~50% progress remains.
        var elapsed = fx.Clock.CurrentTick - player.BreakStartedTick;
        Assert.InRange((int)elapsed, ironNeed / 2 - 1, ironNeed / 2 + 1);
    }

    [Fact]
    public void Dig_auth_skipped_when_BreakTicks_negative()
    {
        // Mirrors InGameAuthInputHandler: need < 0 → do not SubmitDigStart.
        var need = Blocks.BreakTicks(int.MaxValue - 3);
        Assert.Equal(-1, need);
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("nodig");
        Assert.False(player.HasBreakTarget);
        Assert.False(player.TryGetDigAuth(1, 1, 1, out _, out _));
    }
}
