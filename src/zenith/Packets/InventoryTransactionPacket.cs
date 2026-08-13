using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct DecodedTransactionItem
{
    public short NetworkId { get; init; }
    public ushort Count { get; init; }
    public int Meta { get; init; }
    public int BlockRuntimeId { get; init; }
    public int StackNetworkId { get; init; }

    public bool IsEmpty => NetworkId == 0 || Count == 0;
}

readonly struct DecodedInventoryTransactionAction
{
    public uint SourceType { get; init; }
    public byte? WindowId { get; init; }
    public uint? SourceFlags { get; init; }
    public int Slot { get; init; }
    public DecodedTransactionItem OldItem { get; init; }
    public DecodedTransactionItem NewItem { get; init; }
}

/// <summary>
/// InventoryTransaction (0x1e) — decode parcial focado em UseItem (place/destroy).
/// </summary>
class InventoryTransactionPacket : DataPacket
{
    // Zenith only accepts a two-action legacy drop today. Keep the decoder bounded before the
    // handler can reject all other Normal transaction shapes.
    public const int MaxActions = 64;
    public const int MaxLegacySetItemContainers = 32;
    public const int MaxLegacySetItemSlotsPerContainer = 64;
    public const uint SourceContainer = 0;
    public const uint SourceWorld = 2;

    public const int UseClickBlock = 0;
    public const int UseClickAir = 1;
    public const int UseDestroyBlock = 2;
    public const int UseAsAttack = 3;

    public const int ActorInteract = 0;
    public const int ActorAttack = 1;

    public const int ReleaseActionRelease = 0;

    public const uint TypeNormal = 0;
    public const uint TypeMismatch = 1;
    public const uint TypeItemUse = 2;
    public const uint TypeItemUseOnActor = 3;
    public const uint TypeItemRelease = 4;

    public override int Id => (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET;

    public uint TransactionType { get; set; }
    public int LegacyRequestId { get; set; }
    public bool HasLegacySetItemSlots { get; set; }
    public DecodedInventoryTransactionAction[] Actions { get; set; } = [];
    public int UseActionType { get; set; }
    public int BlockX { get; set; }
    public int BlockY { get; set; }
    public int BlockZ { get; set; }
    public byte BlockFace { get; set; }
    public int HotbarSlot { get; set; }
    /// <summary>
    /// Client's runtime-id precondition for the block it clicked. Zero means the client did
    /// not supply a target-state precondition; a nonzero value must agree with the authoritative
    /// world snapshot before a placement is accepted.
    /// </summary>
    public int ClickedBlockRuntimeId { get; set; }
    public int HeldBlockRuntimeId { get; set; }
    public float FromX { get; set; }
    public float FromY { get; set; }
    public float FromZ { get; set; }
    public float HitX { get; set; }
    public float HitY { get; set; }
    public float HitZ { get; set; }

    public long TargetActorRuntimeId { get; set; }
    public int ActorActionType { get; set; }
    public int ReleaseActionType { get; set; }
    /// <summary>
    /// Client-declared stack for the specific item-use transaction. It is diagnostic/input
    /// context only: Gameplay always resolves the authoritative stack from <see cref="HotbarSlot"/>.
    /// </summary>
    public DecodedTransactionItem HeldItem { get; set; }

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        LegacyRequestId = stream.ReadVarInt();
        HasLegacySetItemSlots = stream.ReadBool();
        if (HasLegacySetItemSlots)
        {
            var slotCount = stream.ReadUnsignedVarInt();
            if (slotCount > MaxLegacySetItemContainers)
                throw new InvalidDataException($"InventoryTransaction has {slotCount} legacy containers (max {MaxLegacySetItemContainers}).");
            for (var i = 0; i < slotCount; i++)
            {
                stream.ReadByte();
                var inner = stream.ReadUnsignedVarInt();
                if (inner > MaxLegacySetItemSlotsPerContainer)
                    throw new InvalidDataException($"InventoryTransaction legacy container has {inner} slots (max {MaxLegacySetItemSlotsPerContainer}).");
                for (var j = 0; j < inner; j++) stream.ReadByte();
            }
        }

        stream.ReadBool(); // has transaction type marker
        TransactionType = (uint)stream.ReadUnsignedVarInt();
        stream.ReadBool(); // has actions marker
        var actionCount = stream.ReadUnsignedVarInt();
        if (actionCount > MaxActions)
            throw new InvalidDataException($"InventoryTransaction has {actionCount} actions (max {MaxActions}).");
        var actions = new DecodedInventoryTransactionAction[actionCount];
        for (var i = 0; i < actionCount; i++)
            actions[i] = ReadInventoryAction(ref stream);
        Actions = actions;

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

    private static DecodedInventoryTransactionAction ReadInventoryAction(ref BinaryStream stream)
    {
        var sourceType = (uint)stream.ReadUnsignedVarInt();
        _ = stream.ReadBool(); // required marker around the window optional
        byte? window = stream.ReadBool() ? stream.ReadByte() : null;
        _ = stream.ReadBool(); // required marker around the source-flags optional
        uint? flags = stream.ReadBool() ? (uint)stream.ReadUnsignedVarInt() : null;
        var slot = stream.ReadUnsignedVarInt();
        var oldItem = ReadNetworkItem(ref stream);
        var newItem = ReadNetworkItem(ref stream);
        return new DecodedInventoryTransactionAction
        {
            SourceType = sourceType,
            WindowId = window,
            SourceFlags = flags,
            Slot = slot,
            OldItem = oldItem,
            NewItem = newItem
        };
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
        HeldItem = ReadNetworkItem(ref stream);
        HeldBlockRuntimeId = HeldItem.BlockRuntimeId;

        // position + clicked + ids ignored for gameplay; remaining bytes left unread OK (stream disposed).
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        ClickedBlockRuntimeId = stream.ReadUnsignedVarInt(); // block runtime under cursor
        _ = stream.ReadByte(); // client interact prediction
        _ = stream.ReadByte(); // client cooldown state
    }

    private void SkipItemUseOnActorAction(ref BinaryStream stream)
    {
        TargetActorRuntimeId = stream.ReadUnsignedVarLong();
        ActorActionType = stream.ReadVarInt();
        HotbarSlot = stream.ReadVarInt();
        HeldItem = ReadNetworkItem(ref stream);
        FromX = stream.ReadFloat(BinaryStream.Endianess.Little);
        FromY = stream.ReadFloat(BinaryStream.Endianess.Little);
        FromZ = stream.ReadFloat(BinaryStream.Endianess.Little);
        HitX = stream.ReadFloat(BinaryStream.Endianess.Little);
        HitY = stream.ReadFloat(BinaryStream.Endianess.Little);
        HitZ = stream.ReadFloat(BinaryStream.Endianess.Little);
    }

    private void SkipItemReleaseAction(ref BinaryStream stream)
    {
        ReleaseActionType = stream.ReadVarInt();
        HotbarSlot = stream.ReadVarInt();
        HeldItem = ReadNetworkItem(ref stream);
        FromX = stream.ReadFloat(BinaryStream.Endianess.Little);
        FromY = stream.ReadFloat(BinaryStream.Endianess.Little);
        FromZ = stream.ReadFloat(BinaryStream.Endianess.Little);
    }

    /// <summary>Skip a NetworkItem for AuthInput item-interaction branch.</summary>
    internal static void SkipNetworkItemPublic(ref BinaryStream stream) => _ = ReadNetworkItem(ref stream);

    private static DecodedTransactionItem ReadNetworkItem(ref BinaryStream stream)
    {
        var networkId = stream.ReadShort(BinaryStream.Endianess.Little);
        var count = stream.ReadUShort(BinaryStream.Endianess.Little);
        var meta = stream.ReadUnsignedVarInt();
        var stackNetworkId = stream.ReadBool() ? stream.ReadVarInt() : 0;
        var blockRuntimeId = stream.ReadUnsignedVarInt();
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0) stream.ReadSpan(extraLen);
        return new DecodedTransactionItem
        {
            NetworkId = networkId,
            Count = count,
            Meta = meta,
            StackNetworkId = stackNetworkId,
            BlockRuntimeId = blockRuntimeId
        };
    }

}
