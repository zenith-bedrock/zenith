using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Text (0x09) — chat / tip / system / etc.</summary>
class TextPacket : DataPacket
{
    public const byte TypeRaw = 0;
    public const byte TypeChat = 1;
    public const byte TypeTranslation = 2;
    public const byte TypePopup = 3;
    public const byte TypeJukeboxPopup = 4;
    public const byte TypeTip = 5;
    public const byte TypeSystem = 6;
    public const byte TypeWhisper = 7;
    public const byte TypeAnnouncement = 8;

    private const byte CategoryMessageOnly = 0;
    private const byte CategoryAuthored = 1;
    private const byte CategoryWithParameters = 2;

    public override int Id => (int)ProtocolInfo.TEXT_PACKET;

    public byte Type { get; set; } = TypeChat;
    public bool NeedsTranslation { get; set; }
    public string SourceName { get; set; } = "";
    public string Message { get; set; } = "";
    public string[] Parameters { get; set; } = [];
    public string XboxUserId { get; set; } = "";
    public string PlatformChatId { get; set; } = "";
    public string? FilteredMessage { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteBool(NeedsTranslation);
        writer.WriteByte(CategoryFor(Type));
        writer.WriteByte(Type);

        switch (Type)
        {
            case TypeChat:
            case TypeWhisper:
            case TypeAnnouncement:
                writer.WriteVarString(SourceName);
                writer.WriteVarString(Message);
                break;
            case TypeTranslation:
            case TypePopup:
            case TypeJukeboxPopup:
                writer.WriteVarString(Message);
                writer.WriteUnsignedVarInt(Parameters.Length);
                foreach (var p in Parameters)
                    writer.WriteVarString(p);
                break;
            default:
                writer.WriteVarString(Message);
                break;
        }

        writer.WriteVarString(XboxUserId);
        writer.WriteVarString(PlatformChatId);
        if (FilteredMessage is not null)
        {
            writer.WriteBool(true);
            writer.WriteVarString(FilteredMessage);
        }
        else
        {
            writer.WriteBool(false);
        }

        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        NeedsTranslation = stream.ReadBool();
        stream.ReadByte(); // category
        Type = stream.ReadByte();

        switch (Type)
        {
            case TypeChat:
            case TypeWhisper:
            case TypeAnnouncement:
                SourceName = stream.ReadVarString();
                Message = stream.ReadVarString();
                break;
            case TypeTranslation:
            case TypePopup:
            case TypeJukeboxPopup:
                Message = stream.ReadVarString();
                var count = stream.ReadUnsignedVarInt();
                Parameters = new string[count];
                for (var i = 0; i < count; i++)
                    Parameters[i] = stream.ReadVarString();
                break;
            default:
                Message = stream.ReadVarString();
                break;
        }

        XboxUserId = stream.ReadVarString();
        PlatformChatId = stream.ReadVarString();
        if (stream.ReadBool())
            FilteredMessage = stream.ReadVarString();
        else
            FilteredMessage = null;
    }

    private static byte CategoryFor(byte type) => type switch
    {
        TypeChat or TypeWhisper or TypeAnnouncement => CategoryAuthored,
        TypeTranslation or TypePopup or TypeJukeboxPopup => CategoryWithParameters,
        _ => CategoryMessageOnly
    };
}
