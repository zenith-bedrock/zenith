using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class AbilitiesPacketTests
{
    [Fact]
    public void ValuesForSurvival_matches_pre_refactor_mask()
    {
        uint expected =
            AbilityBits.Bit(AbilityBits.Build) |
            AbilityBits.Bit(AbilityBits.Mine) |
            AbilityBits.Bit(AbilityBits.DoorsAndSwitches) |
            AbilityBits.Bit(AbilityBits.OpenContainers) |
            AbilityBits.Bit(AbilityBits.AttackPlayers) |
            AbilityBits.Bit(AbilityBits.AttackMobs) |
            AbilityBits.Bit(AbilityBits.WalkSpeed);

        Assert.Equal(expected, AbilityData.ValuesForSurvival());
        Assert.Equal(expected, AbilityData.ValuesForGameMode(0));
    }

    [Fact]
    public void ValuesForCreative_includes_mayfly_instabuild_flying()
    {
        var survival = AbilityData.ValuesForSurvival();
        var creative = AbilityData.ValuesForCreative(flying: true);

        Assert.Equal(0u, survival & AbilityBits.Bit(AbilityBits.MayFly));
        Assert.Equal(0u, survival & AbilityBits.Bit(AbilityBits.InstantBuild));
        Assert.Equal(0u, survival & AbilityBits.Bit(AbilityBits.Flying));

        Assert.NotEqual(0u, creative & AbilityBits.Bit(AbilityBits.MayFly));
        Assert.NotEqual(0u, creative & AbilityBits.Bit(AbilityBits.InstantBuild));
        Assert.NotEqual(0u, creative & AbilityBits.Bit(AbilityBits.Flying));
        Assert.Equal(creative, AbilityData.ValuesForGameMode(1));
    }

    [Fact]
    public void ValuesForCreative_flying_off_clears_flying_bit()
    {
        var on = AbilityData.ValuesForCreative(flying: true);
        var off = AbilityData.ValuesForCreative(flying: false);
        Assert.NotEqual(0u, on & AbilityBits.Bit(AbilityBits.Flying));
        Assert.Equal(0u, off & AbilityBits.Bit(AbilityBits.Flying));
        Assert.NotEqual(0u, off & AbilityBits.Bit(AbilityBits.MayFly));
    }

    [Fact]
    public void UpdateAbilities_encode_shape()
    {
        var bytes = UpdateAbilitiesPacket.Create(2, wireGameMode: 0).Encode().ToArray();
        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.UPDATE_ABILITIES_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(2ul, stream.ReadULong(BinaryStream.Endianess.Little));
        Assert.Equal(AbilityBits.PlayerPermissionMember, stream.ReadByte());
        Assert.Equal(AbilityBits.CommandPermissionNormal, stream.ReadByte());
        Assert.Equal(1, stream.ReadByte());
        Assert.Equal(AbilityBits.LayerBase, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal((1u << AbilityBits.Count) - 1, stream.ReadUInt(BinaryStream.Endianess.Little));
        Assert.Equal(AbilityData.ValuesForSurvival(), stream.ReadUInt(BinaryStream.Endianess.Little));
    }

    [Fact]
    public void UpdateAdventureSettings_lan_defaults()
    {
        var bytes = UpdateAdventureSettingsPacket.CreateLanDefaults().Encode().ToArray();
        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.UPDATE_ADVENTURE_SETTINGS_PACKET, stream.ReadUnsignedVarInt());
        Assert.False(stream.ReadBool());
        Assert.False(stream.ReadBool());
        Assert.False(stream.ReadBool());
        Assert.True(stream.ReadBool());
        Assert.True(stream.ReadBool());
    }

    [Fact]
    public void AddPlayer_GameMode_routes_ability_values()
    {
        Assert.Equal(AbilityData.ValuesForSurvival(), AbilityData.ValuesForGameMode(0));
        Assert.Equal(AbilityData.ValuesForCreative(), AbilityData.ValuesForGameMode(1));

        var survival = new AddPlayerPacket { Username = "s", GameMode = 0, ActorRuntimeId = 3 }.Encode();
        var creative = new AddPlayerPacket { Username = "c", GameMode = 1, ActorRuntimeId = 3 }.Encode();
        Assert.True(survival.Length > 16);
        Assert.True(creative.Length > 16);
        // Same framing; differing ability Values bits change payload
        Assert.NotEqual(survival.ToArray(), creative.ToArray());
    }

    [Fact]
    public void RequestAbility_decode_flying_bool()
    {
        var writer = new BinaryStream();
        writer.WriteVarInt(AbilityBits.Flying);
        writer.WriteByte(RequestAbilityPacket.ValueTypeBool);
        writer.WriteBool(true);
        writer.WriteFloat(0f, BinaryStream.Endianess.Little);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        var packet = new RequestAbilityPacket();
        packet.Decode(ref stream);
        Assert.Equal(AbilityBits.Flying, packet.Ability);
        Assert.Equal(RequestAbilityPacket.ValueTypeBool, packet.ValueType);
        Assert.True(packet.BoolValue);
    }
}
