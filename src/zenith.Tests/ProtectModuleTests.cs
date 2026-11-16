using Xunit;
using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Policy;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Modules.Protect;
using Zenith.Player;
using Zenith.World;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>
/// The internal Protect module, driven through the real composition (the fixture's context installs it): a protected edit or
/// container use is refused before any effect, the whole coupled edit is judged, operators may bypass, and the module touches
/// nothing but values and the extension seams.
/// </summary>
public sealed class ProtectModuleTests
{
    private static ProtectModule Protect(IntentTestFixture fx) => fx.Context.Protect;

    private static void Guard(IntentTestFixture fx, int x1, int y1, int z1, int x2, int y2, int z2) =>
        Assert.True(Protect(fx).Regions.TryAdd(ProtectRegion.Between("spawn", x1, y1, z1, x2, y2, z2), out _));

    private static BlockEditSystem Edits(IntentTestFixture fx) =>
        new(fx.Players, fx.World, fx.Context.Durability, fx.Context.BlockPolicies, fx.Context.EventBus);

    private static void Place(IntentTestFixture fx, Player.Player player, int x, int y, int z)
    {
        StandForPlace(player, x, y, z);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(x, y, z, Blocks.Stone, hotbarSlot: 0)));
        Edits(fx).Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public void A_player_cannot_place_inside_a_protected_region_and_loses_nothing()
    {
        var fx = new IntentTestFixture();
        Guard(fx, 0, 60, 0, 10, 80, 10);
        var player = fx.AddInGamePlayer("builder");

        Place(fx, player, 5, 70, 5);

        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(5, 70, 5));
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Placement_outside_the_region_is_untouched()
    {
        var fx = new IntentTestFixture();
        Guard(fx, 0, 60, 0, 10, 80, 10);
        var player = fx.AddInGamePlayer("builder");

        Place(fx, player, 20, 70, 20);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(20, 70, 20));
    }

    [Fact]
    public void A_player_cannot_break_a_protected_block()
    {
        var fx = new IntentTestFixture();
        Guard(fx, 0, 60, 0, 10, 80, 10);
        var player = fx.AddInGamePlayer("breaker", GameMode.Creative);
        fx.World.SetBlock(5, 70, 5, Blocks.Stone);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, World.World.AirRuntimeId, hotbarSlot: -1)));
        Edits(fx).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(5, 70, 5));
    }

    [Fact]
    public void An_operator_may_edit_inside_a_protected_region()
    {
        var fx = new IntentTestFixture();
        Guard(fx, 0, 60, 0, 10, 80, 10);
        var admin = fx.AddOperator("admin");

        Place(fx, admin, 5, 70, 5);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(5, 70, 5));
    }

    [Fact]
    public void A_door_whose_upper_half_is_protected_is_refused_as_a_whole()
    {
        var fx = new IntentTestFixture();
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:wooden_door", out var door));
        // Only the cell above the clicked one is protected.
        Guard(fx, 5, 71, 5, 5, 71, 5);
        var player = fx.AddInGamePlayer("doorman", GameMode.Creative);
        fx.World.SetBlock(5, 69, 5, Blocks.Stone);
        StandForPlace(player, 5, 70, 5);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, door, hotbarSlot: 0)));
        Edits(fx).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(5, 70, 5));
        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(5, 71, 5));
    }

    private static Player.Player NearChest(IntentTestFixture fx, string name, bool guardPartner)
    {
        var player = fx.AddInGamePlayer(name);
        var south = Blocks.ChestForFacing(Blocks.CardinalSouth);
        fx.World.SetBlock(2, 64, 2, south);
        fx.World.SetBlock(3, 64, 2, south);
        fx.World.Chests.Ensure(2, 64, 2);
        fx.World.Chests.Ensure(3, 64, 2);
        player.PositionX = 2.5f;
        player.PositionY = 64;
        player.PositionZ = 2.5f;
        // Guard only one cell of the pair, so the other half must be caught through the partner.
        if (guardPartner) Guard(fx, 3, 64, 2, 3, 64, 2); else Guard(fx, 2, 64, 2, 2, 64, 2);
        return player;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Opening_a_chest_with_either_half_protected_is_refused(bool guardPartner)
    {
        var fx = new IntentTestFixture();
        var player = NearChest(fx, "snoop", guardPartner);

        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(2, 64, 2)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Null(player.Windows.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(2, 64, 2));
        Assert.Equal(0, fx.World.Chests.OpenerCount(3, 64, 2));
    }

    [Fact]
    public void A_window_already_open_when_the_region_appears_is_closed_and_the_request_refused()
    {
        var fx = new IntentTestFixture();
        var player = NearChest(fx, "late", guardPartner: true);
        Protect(fx).Regions.TryRemove("spawn");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 5));
        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(2, 64, 2)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.Windows.OpenChest);

        Guard(fx, 2, 64, 2, 3, 64, 2);
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.OpenContainer(0), 2,
                new WireSlot(InventoryContainerMap.Hotbar, 0), new WireSlot(InventoryContainerMap.Chest, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Null(player.Windows.OpenChest);
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.True(fx.World.Chests.Get(2, 64, 2, 0).IsEmpty);
    }

    [Fact]
    public void Only_a_manager_may_use_the_protect_command_and_it_edits_the_regions()
    {
        var fx = new IntentTestFixture();
        var admin = fx.AddOperator("admin");
        var plain = fx.AddInGamePlayer("plain");
        var commands = fx.Context.Commands;

        Assert.Equal(CommandOutcome.Rejected, commands.Execute(plain, "/protect create spawn 0 60 0 10 80 10").Outcome);
        Assert.True(Protect(fx).Regions.Snapshot.IsEmpty);

        Assert.Equal(CommandOutcome.Applied, commands.Execute(admin, "/protect create spawn 10 80 10 0 60 0").Outcome);
        var region = Assert.Single(Protect(fx).Regions.Snapshot);
        Assert.Equal(new ProtectRegion("spawn", 0, 60, 0, 10, 80, 10), region);
        Assert.Contains("spawn", commands.Execute(admin, "/protect list").Message);
        Assert.Equal(CommandOutcome.Rejected, commands.Execute(admin, "/protect create spawn 0 0 0 1 1 1").Outcome);

        Assert.Equal(CommandOutcome.Applied, commands.Execute(admin, "/protect remove spawn").Outcome);
        Assert.True(Protect(fx).Regions.Snapshot.IsEmpty);
        Assert.Equal(CommandOutcome.Rejected, commands.Execute(admin, "/protect remove spawn").Outcome);
    }

    [Fact]
    public void The_number_of_regions_is_bounded()
    {
        var regions = new ProtectRegions();
        for (var i = 0; i < ProtectRegions.MaxRegions; i++)
            Assert.True(regions.TryAdd(ProtectRegion.Between($"r{i}", 0, 0, 0, 1, 1, 1), out _));

        Assert.False(regions.TryAdd(ProtectRegion.Between("overflow", 0, 0, 0, 1, 1, 1), out _));
    }

    /// <summary>
    /// The module may use permissions, policy and command values only. The IL scan of the gameplay layer cannot vouch for a module,
    /// so its sources are checked for any mention of the live domain or the wire.
    /// </summary>
    [Fact]
    public void The_module_names_no_live_domain_wire_or_session_namespace()
    {
        var root = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(SourcePath())!, "..", "zenith", "Modules", "Protect"));
        Assert.True(Directory.Exists(root), root);
        string[] forbidden =
        [
            "Zenith.Packets", "Zenith.Protocol", "Zenith.Session", "Zenith.Network", "Zenith.Player", "Zenith.World",
            "Zenith.Server", "Zenith.Gameplay.Inventory", "Zenith.Gameplay.Entities", "Zenith.Gameplay.Runtime", "Zenith.Event"
        ];
        var offenders = Directory.EnumerateFiles(root, "*.cs")
            .SelectMany(file => File.ReadLines(file).Select(line => (file, line)))
            .Where(entry => forbidden.Any(ns => entry.line.Contains(ns, StringComparison.Ordinal)))
            .Select(entry => $"{Path.GetFileName(entry.file)}: {entry.line.Trim()}")
            .ToArray();
        Assert.True(offenders.Length == 0, string.Join("; ", offenders));
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string here = "") => here;
}
