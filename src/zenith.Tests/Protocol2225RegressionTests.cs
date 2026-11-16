using Zenith.Gameplay.Ports;
using Zenith.Binary;
using Zenith.Packets;
using Zenith.Session.Handler;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.Raknet.Network;
using Xunit;

namespace Zenith.Tests;

public sealed class Protocol2225RegressionTests
{
    [Theory]
    [InlineData(5f, 0f, 0f)]
    [InlineData(3f, 2f, 2f)]
    [InlineData(3f, 9f, 5f)]
    public void Player_attribute_projection_uses_current_vitals_without_mutating_exhaustion(
        float saturation, float exhaustion, float wireExhaustion)
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("vitals");
        player.Saturation = saturation; player.Exhaustion = exhaustion;
        player.Session.Protocol.Entity.SendPlayerAttributes(player);
        player.Session.Transport.FlushOutgoing();
        var r = new BinaryStream(Assert.Single(OutgoingPackets(fx)));
        Assert.Equal(0x1d, r.ReadUnsignedVarInt());
        Assert.Equal((ulong)player.RuntimeId, (ulong)r.ReadUnsignedVarLong());
        var count = r.ReadUnsignedVarInt();
        var values = new Dictionary<string, float>();
        for (var i = 0; i < count; i++)
        {
            var min = r.ReadFloat(BinaryStream.Endianess.Little);
            var max = r.ReadFloat(BinaryStream.Endianess.Little);
            var value = r.ReadFloat(BinaryStream.Endianess.Little);
            Assert.InRange(value, min, max);
            for (var n = 0; n < 3; n++) _ = r.ReadFloat(BinaryStream.Endianess.Little);
            values.Add(r.ReadVarString(), value);
            Assert.Equal(0, r.ReadUnsignedVarInt());
        }
        Assert.Equal(saturation, values["minecraft:player.saturation"]);
        Assert.Equal(wireExhaustion, values["minecraft:player.exhaustion"]);
        Assert.Equal(exhaustion, player.Exhaustion);
        Assert.Equal(0L, r.ReadUnsignedVarLong()); Assert.True(r.IsEndOfFile);
        r.Dispose();
    }

    [Fact]
    public void Player_effects_and_motion_acknowledge_only_inputs_processed_by_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("input-tick");
        var movement = new MovementSystem(fx.Players);
        var protocol = player.Session.Protocol.Entity;
        player.Intents.SubmitMovementInput(MovementInputState.FromClientAuthInput(0, 65.62f, 0, 0, 0, inputTick: 42));
        Assert.Equal(0UL, player.LastProcessedInputTick);
        protocol.SendMobEffect((ulong)player.RuntimeId, EffectChange.Add, 1, 1, false, 20);
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(42UL, player.LastProcessedInputTick);
        protocol.SendMobEffect((ulong)player.RuntimeId, EffectChange.Add, 1, 1, false, 20);
        protocol.SendKnockback((ulong)player.RuntimeId, 1, 2, 3);
        protocol.SendMobEffect(99999, EffectChange.Add, 1, 1, false, 20); // ordinary actor: tick zero
        player.Intents.SubmitMovementInput(MovementInputState.FromClientAuthInput(0, 65.62f, 0, 0, 0, inputTick: 41));
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(42UL, player.LastProcessedInputTick); // older input cannot move the ack backwards
        player.Session.Transport.FlushOutgoing();
        var effects = new List<ulong>();
        var impulses = new List<ulong>();
        foreach (var bytes in OutgoingPackets(fx))
        {
            var r = new BinaryStream(bytes);
            var id = r.ReadUnsignedVarInt();
            if (id == (int)ProtocolInfo.MOB_EFFECT_PACKET)
            {
                _ = r.ReadUnsignedVarLong(); _ = r.ReadByte(); _ = r.ReadVarInt(); _ = r.ReadVarInt();
                _ = r.ReadBool(); _ = r.ReadVarInt(); effects.Add((ulong)r.ReadUnsignedVarLong());
                _ = r.ReadBool(); Assert.True(r.IsEndOfFile);
            }
            if (id == (int)ProtocolInfo.SET_ACTOR_MOTION_PACKET)
            {
                _ = r.ReadUnsignedVarLong();
                for (var n = 0; n < 3; n++) _ = r.ReadFloat(BinaryStream.Endianess.Little);
                impulses.Add((ulong)r.ReadUnsignedVarLong()); Assert.True(r.IsEndOfFile);
            }
        }
        Assert.Equal([0UL, 42UL, 0UL], effects);
        Assert.Equal([42UL], impulses);
    }

    private static List<byte[]> OutgoingPackets(IntentTestFixture fx)
    {
        var packets = new List<byte[]>();
        foreach (var bytes in fx.Transport.Captured)
        {
            var r = new BinaryStream(bytes, 1, bytes.Length - 1);
            var set = new FrameSet(); set.Decode(ref r);
            foreach (var frame in set.Packets)
            {
                var game = new BinaryStream(frame.Buffer.ToArray());
                Assert.Equal(0xfe, game.ReadByte()); Assert.Equal(PacketCompression.NONE, game.ReadByte());
                while (!game.IsEndOfFile)
                    packets.Add(game.ReadSpan(game.ReadUnsignedVarInt()).ToArray());
            }
        }
        return packets;
    }

    [Theory]
    [InlineData(2UL, "02")]
    [InlineData(127UL, "7F")]
    [InlineData(128UL, "8001")]
    public void MobEffect_runtime_id_is_unsigned(ulong runtimeId, string encodedId)
    {
        var bytes = new MobEffectPacket { TargetRuntimeId = runtimeId }.Encode().ToArray();
        Assert.Equal(Convert.FromHexString("1C" + encodedId + "00000000000000"), bytes);
    }

    // Independent synthetic 2225 body, using the field grammar also verified against our BDS
    // PlayerList captures (293338/293338 bytes). No game assets or reference-client fixture.
    private static byte[] SkinBody() => Convert.FromHexString(
        "0000" +                         // id, patch
        "01000000010000000411223344" +   // fixed image dimensions, var-length pixels
        "01" +                           // one animation (varuint)
        "0100000001000000040A0B0C0D030000803F01" + // image, type 3, frames 1, expression 1
        "000000000000000000" +           // empty cape
        "0000000000" +                   // geometry/version/animation/cape/full ID strings
        "0178563412" +                   // wide arm byte, packed color LE
        "0101700E000000" + "00000000000000000000000000000000" + "010171" + // persona: p, Hair, UUID, default, q
        "01044861697204030201080706050C0B0A09100F0E0D" + // tint map: Hair, four packed colors
        "0001000101");                   // five skin booleans

    [Fact]
    public void Skin_uses_numeric_types_var_counts_and_fixed_four_color_values()
    {
        var golden = SkinBody();
        var reader = new BinaryStream(golden);
        var skin = SerializedSkin.Read(ref reader);
        Assert.True(reader.IsEndOfFile);
        Assert.Equal(SkinArmSize.Wide, skin.ArmSize);
        Assert.Equal(0x12345678, skin.SkinColor);
        Assert.Equal(3u, Assert.Single(skin.Animations).Type);
        Assert.Equal(1u, skin.Animations[0].Expression);
        Assert.Equal(SkinPieceType.Hair, Assert.Single(skin.PersonaPieces).PieceType);
        Assert.Equal("q", skin.PersonaPieces[0].ProductId);
        Assert.Equal([0x01020304, 0x05060708, 0x090a0b0c, 0x0d0e0f10], Assert.Single(skin.TintPieces).Colors);
        var writer = new BinaryStream();
        skin.Write(ref writer);
        Assert.Equal(golden, writer.GetBufferDisposing().ToArray());
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(int.MaxValue, false)]
    public void Text_parameters_are_bounded_before_allocation(int count, bool valid)
    {
        var writer = new BinaryStream();
        writer.WriteBool(false);
        writer.WriteByte(2);
        writer.WriteByte(TextPacket.TypeTranslation);
        writer.WriteVarString("key");
        writer.WriteUnsignedVarInt(count);
        if (valid)
        {
            for (var n = 0; n < count; n++) writer.WriteVarString("p");
            writer.WriteVarString(""); writer.WriteVarString(""); writer.WriteBool(false);
        }
        var body = writer.GetBufferDisposing().ToArray();
        if (!valid) Assert.Throws<InvalidDataException>(() => DecodeText(body));
        else Assert.Equal(4, DecodeText(body).Parameters.Length);
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("text-bounds");
        var input = new BinaryStream(body);
        Assert.True(new InGameSessionHandler().HandleDataPacket(player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.TEXT_PACKET }, ref input));
    }

    [Fact]
    public void Complete_Text_and_PlayerSkin_reject_trailing_bytes_and_truncation()
    {
        var text = new TextPacket { Message = "hello" }.Encode().ToArray()[1..];
        Assert.Throws<InvalidDataException>(() => DecodeText([.. text, 0]));
        Assert.ThrowsAny<Exception>(() => DecodeText(text[..^1]));
        var skin = new PlayerSkinPacket { Uuid = Guid.Empty.ToString(), Skin = SerializedSkin.Default }.Encode().ToArray()[1..];
        Assert.Throws<InvalidDataException>(() => DecodeSkin([.. skin, 0]));
        Assert.ThrowsAny<Exception>(() => DecodeSkin(skin[..^1]));
    }

    [Theory]
    [InlineData("000001000000")]
    [InlineData("010101780001000000")]
    [InlineData("02020178020101000000")]
    [InlineData("0303017801000000")]
    public void Craft_results_read_each_descriptor_variant_without_displacing_the_next_action(string descriptor)
    {
        // One request, ID 1, two actions. CraftResults variant 18/enum20, then CraftRecipe 10/12.
        var bytes = Convert.FromHexString("010202121401" + descriptor + "010A0C4D03010666696C74657200000000");
        var reader = new BinaryStream(bytes);
        var packet = new ItemStackRequestPacket();
        packet.Decode(ref reader);
        var request = Assert.Single(packet.Requests);
        Assert.True(request.AllSupported);
        Assert.Equal(2, request.Actions.Length);
        Assert.Equal(ItemStackRequestPacket.ActionCraftResultsDeprecated, request.Actions[0].ActionType);
        Assert.Equal(ItemStackRequestPacket.ActionCraftRecipe, request.Actions[1].ActionType);
        Assert.Equal(77u, request.Actions[1].RecipeNetId);
        Assert.Equal(3, request.Actions[1].CraftTimes);
        Assert.True(reader.IsEndOfFile);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    public void Invalid_stack_action_variant_cannot_wrap_to_take_or_place(int variant)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); writer.WriteVarInt(1); writer.WriteUnsignedVarInt(1);
        writer.WriteUnsignedVarInt(variant); writer.WriteByte((byte)(variant - 256));
        writer.WriteByte(1);
        for (var n = 0; n < 2; n++)
        {
            writer.WriteByte(0); writer.WriteBool(false); writer.WriteByte(0);
            writer.WriteInt(0, BinaryStream.Endianess.Little);
        }
        writer.WriteUnsignedVarInt(0); writer.WriteInt(0, BinaryStream.Endianess.Little);
        var bytes = writer.GetBufferDisposing().ToArray();
        Assert.Throws<InvalidDataException>(() => DecodeRequest(bytes));
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("invalid-stack-variant");
        var input = new BinaryStream(bytes);
        new InGameSessionHandler().HandleDataPacket(player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET }, ref input);
        Assert.False(player.Intents.TryConsumeInventoryStack(out _));
    }

    private static void DecodeRequest(byte[] bytes)
    {
        var reader = new BinaryStream(bytes); new ItemStackRequestPacket().Decode(ref reader);
    }

    private static TextPacket DecodeText(byte[] body)
    {
        var reader = new BinaryStream(body);
        var packet = new TextPacket(); packet.Decode(ref reader); return packet;
    }
    private static void DecodeSkin(byte[] body)
    {
        var reader = new BinaryStream(body);
        new PlayerSkinPacket().Decode(ref reader);
    }
}
