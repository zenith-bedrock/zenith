using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct PlayerBlockAction
{
    public int Action { get; init; }
    public int BlockX { get; init; }
    public int BlockY { get; init; }
    public int BlockZ { get; init; }
    public int Face { get; init; }
}

/// <summary>
/// UseItem embedded in AuthInput's <c>transaction</c> option (protocol 2193).
/// </summary>
readonly struct AuthItemInteraction
{
    public int ActionType { get; init; }
    public int BlockX { get; init; }
    public int BlockY { get; init; }
    public int BlockZ { get; init; }
    public int Face { get; init; }
    public int HotbarSlot { get; init; }
    public byte Hand { get; init; }
    /// <summary>Client's runtime-id precondition for the clicked block.</summary>
    public int ClickedBlockRuntimeId { get; init; }
    /// <summary>Client-held ItemV4 descriptor; Session validates it against the authoritative hotbar slot.</summary>
    public DecodedTransactionItem HeldItem { get; init; }
}

/// <summary>
/// PlayerAuthInput protocol 2193 decoder. Input Data is a direct enum array, and protocol 2193
/// removes redundant Cereal accessor-presence bytes from conditional fields. Their remaining
/// option bools still gate transaction, stack request, block actions, and vehicle data.
/// </summary>
class PlayerAuthInputPacket : DataPacket
{
    // Input-data flag ordinals (unchanged across the Cereal boundary — only the container
    // encoding changed, from packed bitset to list-of-set-ordinals).
    public const int InputFlagSneaking = 8;
    public const int InputFlagStartSprinting = 25;
    public const int InputFlagStopSprinting = 26;
    public const int InputFlagMissedSwing = 39;
    public const int InputFlagVerticalCollision = 50;

    public const int ActionStartBreak = 0;
    public const int ActionAbortBreak = 1;
    public const int ActionCrackBreak = 18;
    public const int ActionPredictDestroy = 26;
    public const int ActionContinueDestroy = 27;

    public override int Id => (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET;

    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public PlayerBlockAction[] BlockActions { get; set; } = [];
    public AuthItemInteraction? ItemInteraction { get; set; }
    /// <summary>Optional ItemStackRequest carried by PlayerAuthInput (no outer request count).</summary>
    public DecodedItemStackRequest? ItemStackRequest { get; set; }

    /// <summary>Continuous sneak level (input-data flag 8) — §53.</summary>
    public bool InputSneaking { get; set; }

    public bool InputStartSprinting { get; set; }
    public bool InputStopSprinting { get; set; }
    public bool InputMissedSwing { get; set; }
    /// <summary>AuthInput VerticalCollision (flag 50) — peer Absolute ON_GROUND.</summary>
    public bool InputOnGround { get; set; }
    /// <summary>Block action list present — client is performing block interactions this tick.</summary>
    public bool InputPerformBlockActions { get; set; }

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        Pitch = stream.ReadFloat(BinaryStream.Endianess.Little);
        Yaw = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionX = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionY = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionZ = stream.ReadFloat(BinaryStream.Endianess.Little);

        // move_vector + head_yaw
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        // Input Data is a required array in protocol 2193: count followed by enum values.
        InputSneaking = false;
        InputStartSprinting = false;
        InputStopSprinting = false;
        InputMissedSwing = false;
        InputOnGround = false;
        var flagCount = stream.ReadUnsignedVarInt();
        for (var i = 0; i < flagCount; i++)
        {
            var flag = stream.ReadVarInt();
            switch (flag)
            {
                case InputFlagSneaking: InputSneaking = true; break;
                case InputFlagStartSprinting: InputStartSprinting = true; break;
                case InputFlagStopSprinting: InputStopSprinting = true; break;
                case InputFlagMissedSwing: InputMissedSwing = true; break;
                case InputFlagVerticalCollision: InputOnGround = true; break;
            }
        }

        _ = stream.ReadUnsignedVarInt(); // input_mode
        _ = stream.ReadUnsignedVarInt(); // play_mode
        _ = stream.ReadVarInt(); // interaction_model (zigzag32)
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // interact_pitch
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // interact_yaw
        _ = stream.ReadUnsignedVarLong(); // tick
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // pos_delta
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        ItemInteraction = stream.ReadBool() ? ReadTransaction(ref stream) : null;

        ItemStackRequest = stream.ReadBool()
            ? ItemStackRequestPacket.ReadEmbeddedRequest(ref stream)
            : null;

        InputPerformBlockActions = stream.ReadBool();
        BlockActions = InputPerformBlockActions ? ReadBlockActions(ref stream) : [];

        if (stream.ReadBool())
        {
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        }

        if (stream.ReadBool())
            _ = stream.ReadVarLong(); // zigzag64

        // Trailing fields always present since 2168 (no longer client-truncation-optional).
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // analogue_move_vector
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // camera_orientation
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // raw_move_vector
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
    }

    /// <summary>
    /// Player block actions — since 2168 every entry always carries pos+face (no more per-action
    /// gating on whether the action type needs a position).
    /// </summary>
    private static PlayerBlockAction[] ReadBlockActions(ref BinaryStream stream)
    {
        var count = stream.ReadUnsignedVarInt();
        if (count is < 0 or > 100) return [];
        var list = new PlayerBlockAction[count];
        for (var i = 0; i < count; i++)
        {
            var action = stream.ReadVarInt();
            var x = stream.ReadVarInt();
            var y = stream.ReadVarInt();
            var z = stream.ReadVarInt();
            var face = stream.ReadVarInt();
            list[i] = new PlayerBlockAction
            {
                Action = action,
                BlockX = x,
                BlockY = y,
                BlockZ = z,
                Face = face
            };
        }

        return list;
    }

    /// <summary>
    /// AuthInput's embedded <c>transaction</c> option: legacy request/slot context, the base
    /// inventory action array, then the reflected ItemUse transaction.
    /// </summary>
    private static AuthItemInteraction ReadTransaction(ref BinaryStream stream)
    {
        var legacyRequestId = stream.ReadVarInt();
        if (legacyRequestId != 0)
            InventoryTransactionPacket.SkipLegacySetItemSlots(ref stream);

        var actionCount = stream.ReadUnsignedVarInt();
        if (actionCount > InventoryTransactionPacket.MaxActions)
            throw new InvalidDataException($"AuthInput has {actionCount} legacy actions (max {InventoryTransactionPacket.MaxActions}).");
        for (var i = 0; i < actionCount; i++)
            SkipLegacyTransactionAction(ref stream);

        // data: TransactionUseItem
        var actionType = stream.ReadVarInt(); // zigzag32 mapper
        _ = stream.ReadByte(); // trigger_type (u8 mapper)
        var blockX = stream.ReadVarInt();
        var blockY = stream.ReadVarInt();
        var blockZ = stream.ReadVarInt();
        var face = stream.ReadByte(); // u8, not varint
        var hotbar = stream.ReadVarInt();
        var hand = stream.ReadByte();
        var heldItem = ReadNetworkItemStackDescriptor(ref stream); // held_item (ItemV4)
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // player_pos
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // click_pos
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        var clickedBlockRuntimeId = stream.ReadUnsignedVarInt(); // block_runtime_id
        _ = stream.ReadByte(); // client_prediction
        _ = stream.ReadByte(); // client_cooldown_state

        return new AuthItemInteraction
        {
            ActionType = actionType,
            BlockX = blockX,
            BlockY = blockY,
            BlockZ = blockZ,
            Face = face,
            HotbarSlot = hotbar,
            Hand = hand,
            ClickedBlockRuntimeId = clickedBlockRuntimeId,
            HeldItem = heldItem
        };
    }

    private static void SkipLegacyTransactionAction(ref BinaryStream stream)
    {
        _ = stream.ReadUnsignedVarInt(); // source_type (varint mapper)
        if (stream.ReadBool()) _ = stream.ReadByte(); // window_id (i8) option
        if (stream.ReadBool()) _ = stream.ReadUnsignedVarInt(); // flags option
        _ = stream.ReadUnsignedVarInt(); // slot
        _ = ReadNetworkItemStackDescriptor(ref stream); // old_item
        _ = ReadNetworkItemStackDescriptor(ref stream); // new_item
    }

    /// <summary>
    /// ItemV4 (protocol 2168+, matches <see cref="NetworkItemStack.WriteNetworkItemStackDescriptor"/>
    /// field-for-field): i16 LE network id, u16 count, varint meta, bool has-stack-id + bare
    /// zigzag32 if present (no tag byte, ADR §87), varint block runtime id, varint-length extra blob.
    /// </summary>
    private static DecodedTransactionItem ReadNetworkItemStackDescriptor(ref BinaryStream stream)
    {
        var networkId = stream.ReadShort(BinaryStream.Endianess.Little);
        var count = stream.ReadUShort(BinaryStream.Endianess.Little);
        var meta = stream.ReadUnsignedVarInt();
        var stackNetworkId = stream.ReadBool() ? stream.ReadVarInt() : 0;
        var blockRuntimeId = stream.ReadUnsignedVarInt();
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0)
            stream.ReadSpan(extraLen);

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
