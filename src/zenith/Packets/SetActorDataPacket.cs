using Zenith.Binary;

namespace Zenith.Packets;

/// <summary>
/// SetActorData (0x27) — local spawn seed (§34) and peer pose FLAGS updates (§53).
/// </summary>
sealed class SetActorDataPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SET_ACTOR_DATA_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public string Name { get; set; } = "";
    public ulong Tick { get; set; }

    /// <summary>When true, Encode writes FLAGS-only (pose dirty); otherwise full visible-name seed.</summary>
    public bool FlagsOnly { get; set; }

    public bool Sneaking { get; set; }
    public bool Sprinting { get; set; }
    public bool UsingItem { get; set; }
    public bool Swimming { get; set; }
    public bool Burning { get; set; }

    /// <summary>When set, the packet carries only the air supply (and its maximum), not pose or name.</summary>
    public short? AirSupply { get; set; }

    public short AirSupplyMax { get; set; }

    /// <summary>
    /// Non-player FLAGS-only override (e.g. Creeper Ignited/Powered) — when set, <see cref="FlagsOnly"/>
    /// writes this raw bitmask instead of the player pose builder (<see cref="Sneaking"/>/<see cref="Sprinting"/>).
    /// </summary>
    public long? RawFlags { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);

        if (AirSupply is { } air)
            EntityMetadataWriter.WriteAirSupply(ref writer, air, AirSupplyMax);
        else if (FlagsOnly)
            EntityMetadataWriter.WriteFlagsOnly(
                ref writer,
                RawFlags ?? EntityMetadataWriter.BuildPoseFlags(Sneaking, Sprinting, UsingItem, Swimming, Burning));
        else
            EntityMetadataWriter.WriteVisibleNameMetadata(ref writer, Name, Sneaking, Sprinting);

        writer.WriteUnsignedVarInt(0); // IntegerProperties empty
        writer.WriteUnsignedVarInt(0); // FloatProperties empty
        writer.WriteUnsignedVarLong((long)Tick);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
