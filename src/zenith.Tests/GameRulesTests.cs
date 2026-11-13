using Xunit;
using Zenith.Binary;
using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Reference;
using Zenith.Rulebook;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Game rules: the defaults are the ones stock BDS 1.26.63.1 sent (embedded capture), the wire form round-trips to the
/// same bytes, a change replicates and persists, and the rules Zenith claims to enforce really change behaviour.
/// </summary>
public class GameRulesTests
{
    public GameRulesTests() => Blocks.EnsureLoaded();

    private static byte[] CapturedBody() =>
        JoinStaticData.All.First(e => e.Name == JoinStaticNames.GameRules).Body;

    // ---- model ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Defaults_are_the_39_rules_stock_bds_sent_with_their_types()
    {
        var rules = GameRuleDefaults.Create();

        Assert.Equal(39, rules.Snapshot.Count);
        Assert.Equal("commandBlockOutput", rules.Snapshot[0].Name);
        Assert.Equal("tntExplosionDropDecay", rules.Snapshot[^1].Name);
        Assert.False(rules.KeepInventory);
        Assert.True(rules.FallDamage);
        Assert.True(rules.NaturalRegeneration);
        Assert.True(rules.DoTileDrops);
        Assert.Equal(GameRuleKind.Int, rules.Get("maxCommandChainLength").Kind);
        Assert.Equal(65535, rules.Get("maxCommandChainLength").AsInt);
        Assert.Equal(10000, rules.Get("functionCommandLimit").AsInt);
        Assert.Equal(100, rules.Get("playersSleepingPercentage").AsInt);
        Assert.False(rules.Get("showCoordinates").AsBool);
        Assert.All(rules.Snapshot, rule => Assert.True(rule.Editable));
    }

    [Fact]
    public void The_wire_form_round_trips_to_exactly_the_captured_bytes()
    {
        var captured = CapturedBody();

        Assert.Equal(captured, GameRuleCodec.ToBytes(GameRuleCodec.FromBytes(captured)));
    }

    [Fact]
    public void A_list_with_trailing_bytes_or_an_unknown_type_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => GameRuleCodec.FromBytes([.. CapturedBody(), 0]));
        // one rule: name "x", editable, type 9
        Assert.Throws<InvalidDataException>(() => GameRuleCodec.FromBytes([1, 1, (byte)'x', 1, 9, 0]));
    }

    [Fact]
    public void StartGame_embeds_the_rule_list_the_same_way_game_rules_changed_sends_it()
    {
        var rules = GameRuleDefaults.Create().Snapshot;
        var start = new StartGamePacket { GameRules = rules }.Encode().ToArray();
        var changed = new GameRulesChangedPacket { Rules = rules }.Encode().ToArray();

        Assert.True(start.AsSpan().IndexOf(CapturedBody()) >= 0, "start_game does not contain the captured rule list.");
        Assert.True(changed.AsSpan().IndexOf(CapturedBody()) >= 0, "game_rules_changed does not contain the captured rule list.");
    }

    [Fact]
    public void GameRulesChanged_decodes_what_it_encodes()
    {
        var original = GameRuleDefaults.Create().Snapshot;
        var encoded = new GameRulesChangedPacket { Rules = original }.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.GAME_RULES_CHANGED_PACKET, stream.ReadUnsignedVarInt());
        var decoded = new GameRulesChangedPacket();
        decoded.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(original, decoded.Rules);
    }

    [Fact]
    public void Setting_a_rule_changes_only_that_rule_and_bumps_the_version()
    {
        var rules = GameRuleDefaults.Create();
        var before = rules.Snapshot;

        Assert.True(rules.TrySet("keepInventory", "true", out _));

        Assert.True(rules.KeepInventory);
        Assert.Equal(1, rules.Version);
        Assert.Single(rules.Snapshot.Zip(before).Where(p => p.First != p.Second));
        // Setting the value it already has is not a change.
        Assert.True(rules.TrySet("keepInventory", "true", out _));
        Assert.Equal(1, rules.Version);
    }

    [Theory]
    [InlineData("nope", "true")]
    [InlineData("keepInventory", "maybe")]
    [InlineData("spawnRadius", "ten")]
    public void A_bad_name_or_value_is_refused_with_a_reason_and_changes_nothing(string name, string value)
    {
        var rules = GameRuleDefaults.Create();

        Assert.False(rules.TrySet(name, value, out var error));

        Assert.NotEmpty(error);
        Assert.Equal(0, rules.Version);
    }

    [Fact]
    public void A_rule_the_world_marks_not_editable_cannot_be_changed()
    {
        var rules = new GameRules([.. GameRuleDefaults.Load(), GameRule.OfBool("locked", true, editable: false)]);

        Assert.False(rules.TrySet("locked", "false", out var error));
        Assert.Contains("cannot be changed", error);
        Assert.True(rules.Get("locked").AsBool);
    }

    [Fact]
    public void Restore_takes_known_rules_of_the_same_type_and_reports_the_rest()
    {
        var rules = GameRuleDefaults.Create();

        rules.Restore(
        [
            GameRule.OfBool("keepInventory", true),
            GameRule.OfBool("fromTheFuture", true),
            GameRule.OfBool("spawnRadius", true), // known name, but it is an int rule here
        ], out var ignored);

        Assert.True(rules.KeepInventory);
        Assert.Equal(["fromTheFuture", "spawnRadius"], ignored);
        Assert.Equal(10, rules.Get("spawnRadius").AsInt);
    }

    // ---- persistence + replication -------------------------------------------------------------------------------

    [Fact]
    public void A_changed_rule_is_what_the_next_boot_loads()
    {
        var storage = new InMemoryChunkStorage();
        var first = new World.World(storage);
        Assert.True(first.Rules.TrySet("keepInventory", "true", out _));
        Assert.True(first.Rules.TrySet("spawnRadius", "3", out _));
        first.PersistGameRules();

        var second = new World.World(storage);
        second.LoadGameRules();

        Assert.True(second.Rules.KeepInventory);
        Assert.Equal(3, second.Rules.Get("spawnRadius").AsInt);
        Assert.True(second.Rules.FallDamage);
    }

    [Fact]
    public async Task An_unreadable_stored_list_leaves_the_defaults()
    {
        var storage = new InMemoryChunkStorage();
        await storage.PutGameRulesAsync([0xff, 0xff, 0xff]);
        var world = new World.World(storage);

        world.LoadGameRules();

        Assert.Equal(0, world.Rules.Version);
        Assert.False(world.Rules.KeepInventory);
    }

    [Fact]
    public void Sync_system_sends_the_list_to_online_players_once_per_change_and_persists_it()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("alice");
        var system = new GameRuleSyncSystem(fx.World);
        Drain(fx);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(Drain(fx));

        Assert.True(fx.World.Rules.TrySet("pvp", "false", out _));
        system.Tick(fx.Clock, fx.Players.Online);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Single(Drain(fx));
    }

    private static List<byte[]> Drain(IntentTestFixture fx)
    {
        foreach (var p in fx.Players.Online) p.Session.Transport.FlushOutgoing();
        var sent = new List<byte[]>();
        while (fx.Transport.Captured.TryDequeue(out var datagram)) sent.Add(datagram);
        return sent;
    }

    // ---- command -------------------------------------------------------------------------------------------------

    [Fact]
    public void Gamerule_command_shows_and_changes_a_rule()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("op");
        var commands = fx.Context.Commands;

        Assert.Equal("keepInventory = false", commands.Execute(player, "/gamerule keepInventory").Message);
        Assert.Equal("keepInventory = true", commands.Execute(player, "/gamerule KEEPINVENTORY true").Message);
        Assert.True(fx.World.Rules.KeepInventory);
        Assert.Contains("not a valid", commands.Execute(player, "/gamerule spawnRadius lots").Message);
    }

    // ---- enforcement ---------------------------------------------------------------------------------------------

    [Fact]
    public void KeepInventory_lets_a_survival_player_die_with_the_bag_intact()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("keeper");
        Assert.True(fx.World.Rules.TrySet("keepInventory", "true", out _));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 3));

        player.Intents.SubmitMovementInput(MovementInputState.From(3.5f, MovementSystem.VoidRescueY - 1f, 4.5f, 0f, 0f));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(3, player.Inventory.Get(0).Count);
        Assert.Empty(fx.World.FloorDrops.Snapshot());
    }

    [Fact]
    public void FallDamage_off_makes_a_long_fall_harmless()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("floater");
        Assert.True(fx.World.Rules.TrySet("fallDamage", "false", out _));
        var system = new MovementSystem(fx.Players);

        var air = MovementInputState.From(0f, Blocks.FlatSpawnY + 10f, 0f, 0f, 0f);
        air.OnGround = false;
        mover.Intents.SubmitMovementInput(air);
        system.Tick(fx.Clock, fx.Players.Online);
        var land = MovementInputState.From(0f, Blocks.FlatSpawnY, 0f, 0f, 0f);
        land.OnGround = true;
        mover.Intents.SubmitMovementInput(land);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, mover.Health);
    }

    [Fact]
    public void NaturalRegeneration_off_stops_well_fed_healing()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("sore");
        Assert.True(fx.World.Rules.TrySet("naturalRegeneration", "false", out _));
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 20f;
        _ = player.ApplyDamage(DamageSource.Generic, 5f, 0);

        fx.Clock.AdvanceBy(80);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(15f, player.Health);
    }

    [Fact]
    public void DoTileDrops_off_breaks_the_block_without_giving_its_item()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("miner");
        Assert.True(fx.World.Rules.TrySet("doTileDrops", "false", out _));
        player.PositionX = 0.5f; player.PositionY = 90; player.PositionZ = 0.5f;
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        player.SelectedHotbarSlot = 0;
        var need = Blocks.BreakTicks(Blocks.Dirt, default(StackId));
        player.Dig.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need, default(StackId));
        fx.Clock.AdvanceBy(need);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, player.Dig.BreakStartedTick, player.Dig.BreakRequiredTicks)));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.Get(i).IsEmpty);
        Assert.Empty(fx.World.FloorDrops.Snapshot());
    }
}
