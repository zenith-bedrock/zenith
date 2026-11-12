using Xunit;
using Zenith.Binary;
using Zenith.Packets;

namespace Zenith.Tests;

/// <summary>
/// The four small join packets must encode to exactly the bodies stock BDS 1.26.63.1 sent (captured in
/// bds-reference/out/join: <c>026_set_difficulty</c> = 01, <c>027_set_commands_enabled</c> = 00,
/// <c>045_player_hotbar</c> = 00 00 01, <c>053_set_health</c> = 28).
/// </summary>
public class JoinSmallPacketsBdsGoldenTests
{
    private static byte[] Body(DataPacket packet, int id)
    {
        var encoded = packet.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        Assert.Equal(id, stream.ReadUnsignedVarInt());
        var body = stream.ReadRemaining().ToArray();
        stream.Dispose();
        return body;
    }

    [Fact]
    public void SetDifficulty_matches_bds() =>
        Assert.Equal(new byte[] { 0x01 }, Body(new SetDifficultyPacket { Difficulty = 1 }, (int)ProtocolInfo.SET_DIFFICULTY_PACKET));

    [Fact]
    public void SetCommandsEnabled_matches_bds() =>
        Assert.Equal(new byte[] { 0x00 }, Body(new SetCommandsEnabledPacket { Enabled = false }, (int)ProtocolInfo.SET_COMMANDS_ENABLED_PACKET));

    [Fact]
    public void PlayerHotbar_matches_bds() =>
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01 },
            Body(new PlayerHotbarPacket { SelectedSlot = 0, WindowId = 0, SelectSlot = true }, (int)ProtocolInfo.PLAYER_HOTBAR_PACKET));

    [Fact]
    public void SetHealth_matches_bds() =>
        Assert.Equal(new byte[] { 0x28 }, Body(new SetHealthPacket { Health = 20 }, (int)ProtocolInfo.SET_HEALTH_PACKET));

    /// <summary>Both captured clock packets decode and re-encode to the exact captured bytes (the state one and the registry one).</summary>
    [Fact]
    public void SyncWorldClocks_round_trips_both_captured_payloads()
    {
        var captured = Zenith.Reference.JoinStaticData.Named(Zenith.Reference.JoinStaticNames.WorldClocks).ToArray();
        Assert.Equal(2, captured.Length);
        foreach (var entry in captured)
        {
            var stream = new BinaryStream(entry.Body);
            var packet = new SyncWorldClocksPacket();
            packet.Decode(ref stream);
            Assert.True(stream.IsEndOfFile);
            stream.Dispose();

            Assert.Equal(entry.Body, Body(packet, (int)ProtocolInfo.SYNC_WORLD_CLOCKS_PACKET));
        }
    }

    [Fact]
    public void The_overworld_registry_keeps_bds_ids_and_markers_and_takes_our_time()
    {
        var packet = WorldClockRegistry.RegistryPacket(1234);
        var clock = Assert.Single(packet.Clocks);

        Assert.Equal("minecraft:overworld", clock.Name);
        Assert.Equal(1234, clock.Time);
        Assert.Equal(7194480507151251734, clock.Id);
        Assert.Equal(["minecraft:sunrise", "minecraft:night", "minecraft:noon", "minecraft:midnight", "minecraft:day", "minecraft:sunset"],
            clock.Markers.Select(m => m.Name));
        Assert.All(clock.Markers, m => Assert.Equal(24000, m.Period));
        Assert.Equal(clock.Id, Assert.Single(WorldClockRegistry.StatePacket(1234).States).ClockId);
    }

    [Fact]
    public void UnlockedRecipes_round_trips_the_captured_book_byte_for_byte()
    {
        var captured = Zenith.Reference.JoinStaticData.Named(Zenith.Reference.JoinStaticNames.InitialRecipes).Single();
        var stream = new BinaryStream(captured.Body);
        var packet = new UnlockedRecipesPacket();
        packet.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(UnlockedRecipesPacket.InitiallyUnlocked, packet.UnlockType);
        Assert.Equal(20, packet.RecipeIds.Count);
        Assert.Equal(captured.Body, Body(packet, (int)ProtocolInfo.UNLOCKED_RECIPES_PACKET));
    }

    /// <summary>What a new player's book holds here: only recipes this server actually has (a handful of BDS's 20 are furnace recipes).</summary>
    [Fact]
    public void The_initial_book_is_the_bds_list_kept_to_recipes_the_server_has()
    {
        Zenith.World.Blocks.EnsureLoaded();
        var fx = new IntentTestFixture();
        var known = UnlockedRecipesPacket.InitialRecipeIdsOfBds.Where(id => fx.Context.Recipes.TryGetNetId(id, out _)).ToArray();

        Assert.Contains("minecraft:WorkBench_recipeId", known);
        Assert.True(known.Length < UnlockedRecipesPacket.InitialRecipeIdsOfBds.Count);
        Assert.All(known, id => Assert.True(fx.Context.Recipes.TryGetNetId(id, out _)));
    }
}
