using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// DeathInfo (0xbd) — death-screen cause string (outbound, ADR §40).
/// </summary>
sealed class DeathInfoPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.DEATH_INFO_PACKET;

    /// <summary>Translation key or plain cause (e.g. "generic").</summary>
    public string Cause { get; set; } = "generic";

    public string[] Messages { get; set; } = [];

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarString(Cause);
        writer.WriteUnsignedVarInt(Messages.Length);
        foreach (var message in Messages)
            writer.WriteVarString(message);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
