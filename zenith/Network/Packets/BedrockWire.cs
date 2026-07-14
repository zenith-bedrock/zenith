using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>Helpers de wire Bedrock reutilizados por pacotes de entidade/lista.</summary>
static class BedrockWire
{
    public static void WriteUuid(ref BinaryStream writer, Guid uuid)
    {
        Span<byte> rfc = stackalloc byte[16];
        WriteGuidAsRfcBytes(uuid, rfc);
        rfc[..8].Reverse();
        rfc[8..].Reverse();
        writer.Write(rfc);
    }

    public static void WriteByteArray(ref BinaryStream writer, ReadOnlySpan<byte> data)
    {
        writer.WriteUnsignedVarInt(data.Length);
        writer.Write(data);
    }

    /// <summary>Skin placeholder 64×64 branco (RGBA), estático — sem parse de login.</summary>
    public static void WritePlaceholderSkin(ref BinaryStream writer, string skinId)
    {
        writer.WriteVarString(skinId);
        writer.WriteVarString(""); // playFabId
        writer.WriteVarString("""{"geometry":{"default":"geometry.humanoid.custom"}}""");
        WriteSkinImage(ref writer, PlaceholderSkin.Width, PlaceholderSkin.Height, PlaceholderSkin.Pixels);
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // animations
        WriteSkinImage(ref writer, 0, 0, ReadOnlySpan<byte>.Empty); // cape
        writer.WriteVarString(""); // geometryData
        writer.WriteVarString(""); // geometryDataVersion
        writer.WriteVarString(""); // animationData
        writer.WriteVarString(""); // capeId
        writer.WriteVarString(skinId); // fullSkinId
        writer.WriteVarString("wide"); // armSize
        writer.WriteVarString("#0"); // skinColor
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // personaPieces
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // pieceTintColors
        writer.WriteBool(false); // premium
        writer.WriteBool(false); // persona
        writer.WriteBool(false); // capeOnClassic
        writer.WriteBool(true); // isPrimaryUser
        writer.WriteBool(true); // override
    }

    public static void WriteVisibleNameMetadata(ref BinaryStream writer, string name)
    {
        const int metaKeyFlags = 0;
        const int metaKeyColorIndex = 3;
        const int metaKeyName = 4;
        const int metaKeyEffectColor = 8;
        const int metaKeyEffectAmbience = 9;
        const int metaKeyWidth = 53;
        const int metaKeyHeight = 54;
        const int metaKeyAlwaysShowNameTag = 81;

        const int typeByte = 0;
        const int typeInt = 2;
        const int typeFloat = 3;
        const int typeString = 4;
        const int typeLong = 7;

        long flags =
            EntityFlagBit(35) | // breathing
            EntityFlagBit(19) | // canClimb
            EntityFlagBit(48) | // hasCollision
            EntityFlagBit(49) | // affectedByGravity
            EntityFlagBit(14) | // showName
            EntityFlagBit(15); // alwaysShowName

        writer.WriteUnsignedVarInt(8);

        writer.WriteUnsignedVarInt(metaKeyFlags);
        writer.WriteUnsignedVarInt(typeLong);
        writer.WriteVarLong(flags);

        writer.WriteUnsignedVarInt(metaKeyColorIndex);
        writer.WriteUnsignedVarInt(typeByte);
        writer.WriteByte(0);

        writer.WriteUnsignedVarInt(metaKeyName);
        writer.WriteUnsignedVarInt(typeString);
        writer.WriteVarString(name);

        writer.WriteUnsignedVarInt(metaKeyEffectColor);
        writer.WriteUnsignedVarInt(typeInt);
        writer.WriteVarInt(0);

        writer.WriteUnsignedVarInt(metaKeyEffectAmbience);
        writer.WriteUnsignedVarInt(typeByte);
        writer.WriteByte(0);

        writer.WriteUnsignedVarInt(metaKeyWidth);
        writer.WriteUnsignedVarInt(typeFloat);
        writer.WriteFloat(0.6f, BinaryStream.Endianess.Little);

        writer.WriteUnsignedVarInt(metaKeyHeight);
        writer.WriteUnsignedVarInt(typeFloat);
        writer.WriteFloat(1.8f, BinaryStream.Endianess.Little);

        writer.WriteUnsignedVarInt(metaKeyAlwaysShowNameTag);
        writer.WriteUnsignedVarInt(typeByte);
        writer.WriteByte(1);
    }

    public static void WriteEmptyPropertySync(ref BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt(0);
    }

    public static void WriteMinimalAbilities(ref BinaryStream writer, long targetUniqueId)
    {
        const int abilityCount = 19;
        const ushort abilityLayerBase = 1;
        uint allSet = (1u << abilityCount) - 1;
        uint values =
            AbilityBit(0) | // build
            AbilityBit(1) | // mine
            AbilityBit(2) | // doorsAndSwitches
            AbilityBit(3) | // openContainers
            AbilityBit(4) | // attackPlayers
            AbilityBit(5) | // attackMobs
            AbilityBit(14); // walkSpeed

        writer.WriteULong((ulong)targetUniqueId, BinaryStream.Endianess.Little);
        writer.WriteByte(1); // playerPermission member
        writer.WriteByte(0); // commandPermission normal
        writer.WriteByte(1); // layer count
        writer.WriteUShort(abilityLayerBase, BinaryStream.Endianess.Little);
        writer.WriteUInt(allSet, BinaryStream.Endianess.Little);
        writer.WriteUInt(values, BinaryStream.Endianess.Little);
        writer.WriteFloat(0.05f, BinaryStream.Endianess.Little); // fly
        writer.WriteFloat(1.0f, BinaryStream.Endianess.Little); // vertical fly
        writer.WriteFloat(0.1f, BinaryStream.Endianess.Little); // walk
    }

    public static void WriteAirItem(ref BinaryStream writer) => writer.WriteVarInt(0);

    private static void WriteSkinImage(ref BinaryStream writer, uint width, uint height, ReadOnlySpan<byte> pixels)
    {
        writer.WriteUInt(width, BinaryStream.Endianess.Little);
        writer.WriteUInt(height, BinaryStream.Endianess.Little);
        WriteByteArray(ref writer, pixels);
    }

    private static long EntityFlagBit(int index) => 1L << index;
    private static uint AbilityBit(int index) => 1u << index;

    private static void WriteGuidAsRfcBytes(Guid uuid, Span<byte> destination)
    {
        var mixed = uuid.ToByteArray();
        destination[0] = mixed[3];
        destination[1] = mixed[2];
        destination[2] = mixed[1];
        destination[3] = mixed[0];
        destination[4] = mixed[5];
        destination[5] = mixed[4];
        destination[6] = mixed[7];
        destination[7] = mixed[6];
        mixed.AsSpan(8, 8).CopyTo(destination[8..]);
    }

    private static class PlaceholderSkin
    {
        public const uint Width = 64;
        public const uint Height = 64;
        public static readonly byte[] Pixels = CreateWhiteRgba();

        private static byte[] CreateWhiteRgba()
        {
            var pixels = new byte[Width * Height * 4];
            pixels.AsSpan().Fill(0xff);
            return pixels;
        }
    }
}
