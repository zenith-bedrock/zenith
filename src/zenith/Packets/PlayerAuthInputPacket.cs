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
/// UseItem embedded in AuthInput when <see cref="PlayerAuthInputPacket.InputFlagPerformItemInteraction"/> is set.
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
/// Decode continues past the position prefix so survival destroy via flag PerformBlockActions works.
/// </summary>
class PlayerAuthInputPacket : DataPacket
{
    public const int InputFlagSneaking = 8;
    public const int InputFlagStartSprinting = 25;
    public const int InputFlagStopSprinting = 26;
    public const int InputFlagPerformItemInteraction = 34;
    public const int InputFlagPerformBlockActions = 35;
    public const int InputFlagPerformItemStackRequest = 36;
    public const int InputFlagMissedSwing = 39;
    public const int InputFlagClientPredictedVehicle = 45;
    /// <summary>Client vertical collision — strongly correlates with on-ground (protocol ≥729).</summary>
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

    /// <summary>Continuous sneak level (AuthInput bit 8) — §53.</summary>
    public bool InputSneaking { get; set; }

    public bool InputStartSprinting { get; set; }
    public bool InputStopSprinting { get; set; }
    public bool InputMissedSwing { get; set; }
    /// <summary>AuthInput VerticalCollision (bit 50) — peer Absolute ON_GROUND.</summary>
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

        Span<byte> inputFlags = stackalloc byte[16];
        var inputLen = 0;
        while (inputLen < inputFlags.Length)
        {
            var b = stream.ReadByte();
            inputFlags[inputLen++] = b;
            if ((b & 0x80) == 0) break;
        }

        if (inputLen > 0 && (inputFlags[inputLen - 1] & 0x80) != 0)
        {
            while (true)
            {
                var b = stream.ReadByte();
                if ((b & 0x80) == 0) break;
            }
        }

        inputFlags = inputFlags[..inputLen];
        InputSneaking = InputBitsetTest(inputFlags, InputFlagSneaking);
        InputStartSprinting = InputBitsetTest(inputFlags, InputFlagStartSprinting);
        InputStopSprinting = InputBitsetTest(inputFlags, InputFlagStopSprinting);
        InputMissedSwing = InputBitsetTest(inputFlags, InputFlagMissedSwing);
        InputOnGround = InputBitsetTest(inputFlags, InputFlagVerticalCollision);
        InputPerformBlockActions = InputBitsetTest(inputFlags, InputFlagPerformBlockActions);

        _ = stream.ReadUnsignedVarInt(); // input_mode
        _ = stream.ReadUnsignedVarInt(); // play_mode
        _ = stream.ReadUnsignedVarInt(); // interaction_model
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // interact_pitch
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // interact_yaw
        _ = stream.ReadUnsignedVarLong(); // tick
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // delta x
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        if (InputBitsetTest(inputFlags, InputFlagPerformItemInteraction))
            ItemInteraction = ReadUseItemTransactionData(ref stream);
        else
            ItemInteraction = null;

        if (InputBitsetTest(inputFlags, InputFlagPerformItemStackRequest))
            ItemStackRequestPacket.SkipEmbeddedRequest(ref stream);

        if (InputBitsetTest(inputFlags, InputFlagPerformBlockActions))
            BlockActions = ReadBlockActions(ref stream);
        else
            BlockActions = [];

        if (InputBitsetTest(inputFlags, InputFlagClientPredictedVehicle))
        {
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
            _ = stream.ReadVarLong();
        }

        // Trailing fields optional if client truncated; best-effort.
        if (!stream.IsEndOfFile)
        {
            try
            {
                _ = stream.ReadFloat(BinaryStream.Endianess.Little); // analogue_move
                _ = stream.ReadFloat(BinaryStream.Endianess.Little);
                if (!stream.IsEndOfFile)
                {
                    _ = stream.ReadFloat(BinaryStream.Endianess.Little); // camera
                    _ = stream.ReadFloat(BinaryStream.Endianess.Little);
                    _ = stream.ReadFloat(BinaryStream.Endianess.Little);
                }

                if (!stream.IsEndOfFile)
                {
                    _ = stream.ReadFloat(BinaryStream.Endianess.Little); // raw_move
                    _ = stream.ReadFloat(BinaryStream.Endianess.Little);
                }
            }
            catch
            {
                // Trailing optional; block actions already captured.
            }
        }
    }

    private static PlayerBlockAction[] ReadBlockActions(ref BinaryStream stream)
    {
        var count = stream.ReadVarInt();
        if (count is < 0 or > 64) return [];
        var list = new PlayerBlockAction[count];
        for (var i = 0; i < count; i++)
        {
            var action = stream.ReadVarInt();
            var x = 0;
            var y = 0;
            var z = 0;
            var face = 0;
            if (action is ActionStartBreak or ActionAbortBreak or ActionCrackBreak
                or ActionPredictDestroy or ActionContinueDestroy)
            {
                x = stream.ReadVarInt();
                y = stream.ReadVarInt();
                z = stream.ReadVarInt();
                face = stream.ReadVarInt();
            }

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
    /// AuthInput flag bitset: 7 data bits per byte, continuation bit 0x80.
    /// Allocates — Decode inlines a stackalloc path (ref struct + Span cannot share a helper).
    /// </summary>
    internal static byte[] ReadInputBitset(ref BinaryStream stream)
    {
        Span<byte> tmp = stackalloc byte[16];
        var count = 0;
        while (count < tmp.Length)
        {
            var b = stream.ReadByte();
            tmp[count++] = b;
            if ((b & 0x80) == 0) break;
        }

        if (count > 0 && (tmp[count - 1] & 0x80) != 0)
        {
            while (true)
            {
                var b = stream.ReadByte();
                if ((b & 0x80) == 0) break;
            }
        }

        return tmp[..count].ToArray();
    }

    internal static bool InputBitsetTest(ReadOnlySpan<byte> bitset, int flag)
    {
        var idx = flag / 7;
        var pos = flag % 7;
        if (idx >= bitset.Length) return false;
        return (bitset[idx] & (1 << pos)) != 0;
    }

    /// <summary>Decode AuthInput item-interaction branch (legacy request + UseItem fields).</summary>
    private static AuthItemInteraction ReadUseItemTransactionData(ref BinaryStream stream)
    {
        var legacyRequestId = stream.ReadVarInt();
        if (legacyRequestId < -1 && (legacyRequestId & 1) == 0)
        {
            var legacySlotCount = stream.ReadUnsignedVarInt();
            for (var i = 0; i < legacySlotCount; i++)
            {
                _ = stream.ReadByte(); // container id
                var slotsLen = stream.ReadUnsignedVarInt();
                if (slotsLen > 0)
                    stream.ReadSpan(slotsLen);
            }
        }

        var actionCount = stream.ReadUnsignedVarInt();
        for (var i = 0; i < actionCount; i++)
            SkipInventoryActionOld(ref stream);

        var actionType = (int)stream.ReadUnsignedVarInt();
        _ = stream.ReadUnsignedVarInt(); // TriggerType
        var blockX = stream.ReadVarInt();
        var blockY = stream.ReadVarInt();
        var blockZ = stream.ReadVarInt();
        var face = stream.ReadVarInt();
        var hotbar = stream.ReadVarInt();
        SkipItemInstance(ref stream); // HeldItem
        for (var i = 0; i < 6; i++)
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadUnsignedVarInt(); // BlockRuntimeID
        _ = stream.ReadByte(); // ClientPrediction
        _ = stream.ReadByte(); // ClientCooldownState

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

    private const uint InventoryActionSourceContainer = 0;
    private const uint InventoryActionSourceWorld = 2;
    private const uint InventoryActionSourceTodo = 99999;

    private static void SkipInventoryActionOld(ref BinaryStream stream)
    {
        var sourceType = (uint)stream.ReadUnsignedVarInt();
        if (sourceType is InventoryActionSourceContainer or InventoryActionSourceTodo)
            _ = stream.ReadVarInt(); // WindowID
        else if (sourceType == InventoryActionSourceWorld)
            _ = stream.ReadUnsignedVarInt(); // SourceFlags

        _ = stream.ReadUnsignedVarInt(); // InventorySlot
        SkipItemInstance(ref stream);
        SkipItemInstance(ref stream);
    }

    /// <summary>Skip legacy ItemInstance (VarInt network id) inside AuthInput item interaction.</summary>
    private static void SkipItemInstance(ref BinaryStream stream)
    {
        var networkId = stream.ReadVarInt();
        if (networkId == 0)
            return;

        _ = stream.ReadUShort(BinaryStream.Endianess.Little);
        _ = stream.ReadUnsignedVarInt(); // meta
        if (stream.ReadBool())
            _ = stream.ReadVarInt(); // stack network id
        _ = stream.ReadVarInt(); // block runtime
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0)
            stream.ReadSpan(extraLen);
    }
}
