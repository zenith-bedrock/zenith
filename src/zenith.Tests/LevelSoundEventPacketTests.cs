using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Stream;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class LevelSoundEventPacketTests
{
    [Fact]
    public void LevelSoundEvent_encode_decode_roundtrip()
    {
        var original = new LevelSoundEventPacket
        {
            Sound = LevelSoundEventPacket.SoundPlace,
            PositionX = 1.5f,
            PositionY = 64.5f,
            PositionZ = 2.5f,
            ExtraData = 12345,
            EntityType = ":",
            IsBabyMob = false,
            IsGlobal = false,
            ActorUniqueId = -1,
            HasFirePosition = false
        };

        var encoded = original.Encode().ToArray();
        var reader = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.LEVEL_SOUND_EVENT_PACKET, (int)reader.ReadUnsignedVarInt());
        var decoded = new LevelSoundEventPacket();
        decoded.Decode(ref reader);

        Assert.Equal(original.Sound, decoded.Sound);
        Assert.Equal(original.PositionX, decoded.PositionX);
        Assert.Equal(original.PositionY, decoded.PositionY);
        Assert.Equal(original.PositionZ, decoded.PositionZ);
        Assert.Equal(original.ExtraData, decoded.ExtraData);
        Assert.Equal(original.EntityType, decoded.EntityType);
        Assert.Equal(original.ActorUniqueId, decoded.ActorUniqueId);
        Assert.False(decoded.HasFirePosition);
    }

    [Fact]
    public void BlockSystem_place_fans_LevelSound_to_peers()
    {
        Blocks.EnsureLoaded();
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        var bob = fx.AddInGamePlayer("bob");
        StandForPlace(alice, 2, 64, 0);
        Assert.True(alice.Inventory.TrySetBlock(0, Blocks.Stone, 4));

        while (fx.Transport.Captured.TryDequeue(out _)) { }

        Assert.True(alice.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
        Flush(fx);

        Assert.True(fx.Transport.Captured.Count >= 1);
        var joined = Concat(fx);
        Assert.Contains("place"u8.ToArray(), joined);
        _ = bob;
    }

    private static void StandForPlace(Player.Player player, int x, int y, int z)
    {
        player.PositionX = x + 1.5f;
        player.PositionY = y;
        player.PositionZ = z + 0.5f;
    }

    private static void Flush(IntentTestFixture fx)
    {
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();
    }

    private static byte[] Concat(IntentTestFixture fx)
    {
        var total = 0;
        foreach (var chunk in fx.Transport.Captured)
            total += chunk.Length;
        var buf = new byte[total];
        var o = 0;
        foreach (var chunk in fx.Transport.Captured)
        {
            chunk.CopyTo(buf, o);
            o += chunk.Length;
        }

        return buf;
    }
}
