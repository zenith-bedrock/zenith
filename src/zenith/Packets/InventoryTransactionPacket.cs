using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// InventoryTransaction (0x1e) — decode parcial focado em UseItem (place/destroy).
/// </summary>
class InventoryTransactionPacket : DataPacket
{
    public const int UseClickBlock = 0;
    public const int UseDestroyBlock = 2;

    public const uint TypeNormal = 0;
    public const uint TypeMismatch = 1;
    public const uint TypeItemUse = 2;
    public const uint TypeItemUseOnActor = 3;
    public const uint TypeItemRelease = 4;

    public override int Id => (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET;

    public uint TransactionType { get; set; }
    public int UseActionType { get; set; }
    public int BlockX { get; set; }
    public int BlockY { get; set; }
    public int BlockZ { get; set; }
    public byte BlockFace { get; set; }
    public int HotbarSlot { get; set; }
    public int HeldBlockRuntimeId { get; set; }
    public float FromX { get; set; }
    public float FromY { get; set; }
    public float FromZ { get; set; }
    public float HitX { get; set; }
    public float HitY { get; set; }
    public float HitZ { get; set; }

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        stream.ReadVarInt(); // legacyRequestId
        if (stream.ReadBool())
        {
            var slotCount = stream.ReadUnsignedVarInt();
            for (var i = 0; i < slotCount; i++)
            {
                stream.ReadByte();
                var inner = stream.ReadUnsignedVarInt();
                for (var j = 0; j < inner; j++) stream.ReadByte();
            }
        }

        stream.ReadBool(); // has transaction type marker
        TransactionType = (uint)stream.ReadUnsignedVarInt();
        stream.ReadBool(); // has actions marker
        var actionCount = stream.ReadUnsignedVarInt();
        for (var i = 0; i < actionCount; i++)
            SkipInventoryAction(ref stream);

        // if (TransactionType != TypeUseItem) return;

        switch (TransactionType)
        {
            case TypeItemUse:
                SkipInventoryItemUseAction(ref stream);
                break;
            case TypeItemUseOnActor:
                SkipItemUseOnActorAction(ref stream);
                break;
            case TypeItemRelease:
                SkipItemReleaseAction(ref stream);
                break;
        }
    }

    private static void SkipInventoryAction(ref BinaryStream stream)
    {
        stream.ReadUnsignedVarInt(); // source type
        stream.ReadBool();
        if (stream.ReadBool()) stream.ReadByte(); // window
        stream.ReadBool();
        if (stream.ReadBool()) stream.ReadUnsignedVarInt(); // flags
        stream.ReadUnsignedVarInt(); // slot
        SkipNetworkItem(ref stream);
        SkipNetworkItem(ref stream);
    }

    private void SkipInventoryItemUseAction(ref BinaryStream stream)
    {
        UseActionType = stream.ReadVarInt();
        stream.ReadByte(); // trigger
        BlockX = stream.ReadVarInt();
        BlockY = stream.ReadVarInt();
        BlockZ = stream.ReadVarInt();
        BlockFace = stream.ReadByte();
        HotbarSlot = stream.ReadVarInt();
        HeldBlockRuntimeId = SkipNetworkItemReadBlockRuntimeId(ref stream);

        // position + clicked + ids ignored for gameplay; remaining bytes left unread OK (stream disposed).
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        _ = stream.ReadUnsignedVarInt(); // block runtime under cursor
        _ = stream.ReadByte(); // client interact prediction
        _ = stream.ReadByte(); // client cooldown state
    }

    private void SkipItemUseOnActorAction(ref BinaryStream stream)
    {
        _ = stream.ReadUnsignedVarLong(); // actor runtime id
        _ = stream.ReadVarInt(); // action type (Interact/Attack)
        _ = stream.ReadVarInt(); // slot
        SkipNetworkItem(ref stream);
        FromX = stream.ReadFloat(BinaryStream.Endianess.Little);
        FromY = stream.ReadFloat(BinaryStream.Endianess.Little);
        FromZ = stream.ReadFloat(BinaryStream.Endianess.Little);
        HitX = stream.ReadFloat(BinaryStream.Endianess.Little);
        HitY = stream.ReadFloat(BinaryStream.Endianess.Little);
        HitZ = stream.ReadFloat(BinaryStream.Endianess.Little);
    }

    private void SkipItemReleaseAction(ref BinaryStream stream)
    {
        _ = stream.ReadVarInt(); // action type (Release/Use)
        _ = stream.ReadVarInt(); // slot
        SkipNetworkItem(ref stream);
        FromX = stream.ReadFloat(BinaryStream.Endianess.Little);
        FromY = stream.ReadFloat(BinaryStream.Endianess.Little);
        FromZ = stream.ReadFloat(BinaryStream.Endianess.Little);
    }

    private static void SkipNetworkItem(ref BinaryStream stream) => SkipNetworkItemPublic(ref stream);

    /// <summary>Skip a NetworkItem for AuthInput item-interaction branch.</summary>
    internal static void SkipNetworkItemPublic(ref BinaryStream stream)
    {
        stream.ReadShort(BinaryStream.Endianess.Little); // id
        stream.ReadUShort(BinaryStream.Endianess.Little); // count
        stream.ReadUnsignedVarInt(); // meta
        if (stream.ReadBool())
        {
            stream.ReadUnsignedVarInt();
            stream.ReadVarInt();
        }

        stream.ReadUnsignedVarInt(); // block runtime
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0) stream.ReadSpan(extraLen);
    }

    private static int SkipNetworkItemReadBlockRuntimeId(ref BinaryStream stream)
    {
        stream.ReadShort(BinaryStream.Endianess.Little);
        stream.ReadUShort(BinaryStream.Endianess.Little);
        stream.ReadUnsignedVarInt();
        if (stream.ReadBool())
        {
            stream.ReadUnsignedVarInt();
            stream.ReadVarInt();
        }

        var blockRuntimeId = stream.ReadUnsignedVarInt();
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0) stream.ReadSpan(extraLen);
        return blockRuntimeId;
    }
}
