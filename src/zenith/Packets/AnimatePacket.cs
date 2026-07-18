using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Animate (0x2c) — server-owned arm swing / crits to peers (§53). Protocol 1001 wire:
/// action u8 + runtime id + data f32 + optional swingSource string.
/// Inbound client Animate stays quiet (no rebroadcast).
/// </summary>
sealed class AnimatePacket : DataPacket
{
    public const int ActionSwingArm = 1;
    public const int ActionStopSleep = 3;
    public const int ActionCriticalHit = 4;
    public const int ActionMagicCriticalHit = 5;

    public override int Id => (int)ProtocolInfo.ANIMATE_PACKET;

    public int Action { get; set; }
    public ulong ActorRuntimeId { get; set; }

    /// <summary>Always present on wire (rowing used this historically; SwingArm uses 0).</summary>
    public float Data { get; set; }

    /// <summary>Optional; e.g. "attack", "mine". Null → optional bool false.</summary>
    public string? SwingSource { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteByte((byte)Action);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        writer.WriteFloat(Data, BinaryStream.Endianess.Little);
        if (SwingSource is { } src)
        {
            writer.WriteBool(true);
            writer.WriteVarString(src);
        }
        else
        {
            writer.WriteBool(false);
        }

        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        Action = stream.ReadByte();
        ActorRuntimeId = (ulong)stream.ReadUnsignedVarLong();
        Data = stream.ReadFloat(BinaryStream.Endianess.Little);
        if (stream.ReadBool())
            SwingSource = stream.ReadVarString();
        else
            SwingSource = null;
    }
}
