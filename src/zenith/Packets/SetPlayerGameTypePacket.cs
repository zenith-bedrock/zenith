using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>SetPlayerGameType (0x3E) — runtime player game mode (ADR §52).</summary>
sealed class SetPlayerGameTypePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SET_PLAYER_GAME_TYPE_PACKET;

    public int GameType { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(GameType);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        GameType = stream.ReadVarInt();
    }
}
