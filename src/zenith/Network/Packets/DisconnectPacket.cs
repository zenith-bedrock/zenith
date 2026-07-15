using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

class DisconnectPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.DISCONNECT_PACKET;

    /// <summary>Bedrock DisconnectFailReason base (iota 0). More values when outbound kick needs them.</summary>
    public const int ReasonUnknown = 0;

    public int Reason;
    public bool HideDisconnectionScreen;
    public string Message = "";
    public string FilteredMessage = "";

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(Reason);
        writer.WriteBool(HideDisconnectionScreen);
        if (!HideDisconnectionScreen)
        {
            writer.WriteVarString(Message);
            writer.WriteVarString(FilteredMessage);
        }
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        Reason = stream.ReadVarInt();
        HideDisconnectionScreen = stream.ReadBool();
        if (!HideDisconnectionScreen)
        {
            Message = stream.ReadVarString();
            FilteredMessage = stream.ReadVarString();
        }
    }
}
