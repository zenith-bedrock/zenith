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
/// UseItem embedded in AuthInput's <c>transaction</c> option (protocol 2168+, ADR §89).
/// </summary>
readonly struct AuthItemInteraction
{
    public int ActionType { get; init; }
    public int BlockX { get; init; }
    public int BlockY { get; init; }
    public int BlockZ { get; init; }
    public int Face { get; init; }
    public int HotbarSlot { get; init; }
}

/// <summary>
/// PlayerAuthInput — movement + optional BlockActions (server-authoritative block breaking).
/// Protocol 2168+ (Cereal, ADR §89): the old packed-bitset input flags became a Cereal list of
/// enum ordinals, and every conditional sub-structure (transaction / item stack request / block
/// actions / vehicle rotation / predicted vehicle) became a genuine <c>option</c> with its own
/// presence bool — gated by that bool directly, no longer by testing an input-flag bit. Verified
/// byte-for-byte against a live capture cross-checked with minecraft-data's 1.26.40 protocol.json
/// (every "option" reads its own bool; a preceding same-purpose "_presence" field exists on the
/// wire too but is decorative — real presence is always the option's own bool).
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

        // input_data: option<array<uvarint32 count, zigzag32 flag ordinal>> — the option's own
        // bool is the only presence marker (no preceding "_presence" companion for this one).
        InputSneaking = false;
        InputStartSprinting = false;
        InputStopSprinting = false;
        InputMissedSwing = false;
        InputOnGround = false;
        if (stream.ReadBool())
        {
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

        _ = stream.ReadBool(); // transaction_presence (decorative, real gate is the option below)
        ItemInteraction = stream.ReadBool() ? ReadTransaction(ref stream) : null;

        _ = stream.ReadBool(); // item_stack_request_presence (decorative)
        if (stream.ReadBool())
            ItemStackRequestPacket.SkipEmbeddedRequest(ref stream);

        _ = stream.ReadBool(); // block_action_presence (decorative)
        InputPerformBlockActions = stream.ReadBool();
        BlockActions = InputPerformBlockActions ? ReadBlockActions(ref stream) : [];

        _ = stream.ReadBool(); // vehicle_rotation_presence (decorative)
        if (stream.ReadBool())
        {
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        }

        _ = stream.ReadBool(); // predicted_vehicle_presence (decorative)
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
    /// AuthInput's embedded <c>transaction</c> option — TransactionLegacy (unused, always
    /// skipped) + optional legacy inventory actions (unused) + the real payload: TransactionUseItem.
    /// </summary>
    private static AuthItemInteraction ReadTransaction(ref BinaryStream stream)
    {
        _ = stream.ReadVarInt(); // legacy_request_id
        if (stream.ReadBool()) // legacy_transactions option
        {
            var containerCount = stream.ReadUnsignedVarInt();
            for (var i = 0; i < containerCount; i++)
            {
                _ = stream.ReadByte(); // container_id
                var slotCount = stream.ReadUnsignedVarInt();
                for (var j = 0; j < slotCount; j++)
                    _ = stream.ReadByte(); // slot_id
            }
        }

        _ = stream.ReadBool(); // actions_presence (decorative)
        if (stream.ReadBool()) // actions option
        {
            var actionCount = stream.ReadUnsignedVarInt();
            for (var i = 0; i < actionCount; i++)
                SkipLegacyTransactionAction(ref stream);
        }

        // data: TransactionUseItem
        var actionType = stream.ReadVarInt(); // zigzag32 mapper
        _ = stream.ReadByte(); // trigger_type (u8 mapper)
        var blockX = stream.ReadVarInt();
        var blockY = stream.ReadVarInt();
        var blockZ = stream.ReadVarInt();
        var face = stream.ReadByte(); // u8, not varint
        var hotbar = stream.ReadVarInt();
        SkipNetworkItemStackDescriptor(ref stream); // held_item (ItemV4)
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // player_pos
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // click_pos
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadUnsignedVarInt(); // block_runtime_id
        _ = stream.ReadByte(); // client_prediction
        _ = stream.ReadByte(); // client_cooldown_state

        return new AuthItemInteraction
        {
            ActionType = actionType,
            BlockX = blockX,
            BlockY = blockY,
            BlockZ = blockZ,
            Face = face,
            HotbarSlot = hotbar
        };
    }

    private static void SkipLegacyTransactionAction(ref BinaryStream stream)
    {
        _ = stream.ReadUnsignedVarInt(); // source_type (varint mapper)
        _ = stream.ReadBool(); // container_presence (decorative)
        if (stream.ReadBool()) _ = stream.ReadByte(); // window_id (i8) option
        _ = stream.ReadBool(); // flag_presence (decorative)
        if (stream.ReadBool()) _ = stream.ReadUnsignedVarInt(); // flags option
        _ = stream.ReadUnsignedVarInt(); // slot
        SkipNetworkItemStackDescriptor(ref stream); // old_item
        SkipNetworkItemStackDescriptor(ref stream); // new_item
    }

    /// <summary>
    /// ItemV4 (protocol 2168+, matches <see cref="NetworkItemStack.WriteNetworkItemStackDescriptor"/>
    /// field-for-field): i16 LE network id, u16 count, varint meta, bool has-stack-id + bare
    /// zigzag32 if present (no tag byte, ADR §87), varint block runtime id, varint-length extra blob.
    /// </summary>
    private static void SkipNetworkItemStackDescriptor(ref BinaryStream stream)
    {
        _ = stream.ReadShort(BinaryStream.Endianess.Little); // network_id
        _ = stream.ReadUShort(BinaryStream.Endianess.Little); // count
        _ = stream.ReadUnsignedVarInt(); // metadata
        if (stream.ReadBool()) // has_stack_id
            _ = stream.ReadVarInt(); // stack_id
        _ = stream.ReadUnsignedVarInt(); // block_runtime_id
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0)
            stream.ReadSpan(extraLen);
    }
}
