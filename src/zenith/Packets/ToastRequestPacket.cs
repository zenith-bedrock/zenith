using Zenith.Raknet.Stream;

namespace Zenith.Packets;

sealed class ToastRequestPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.TOAST_REQUEST_PACKET;

    public string Title { get; set; }
    public string Content { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        Title = stream.ReadVarString();
        Content = stream.ReadVarString();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();

        writer.WriteVarString(Title);
        writer.WriteVarString(Content);

        return writer.GetBufferDisposing();
    }
}
