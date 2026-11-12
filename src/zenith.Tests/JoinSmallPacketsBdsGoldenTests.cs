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
}
