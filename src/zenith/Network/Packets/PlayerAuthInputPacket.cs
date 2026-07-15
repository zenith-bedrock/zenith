using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

readonly struct PlayerBlockAction
{
    public int Action { get; init; }
    public int BlockX { get; init; }
    public int BlockY { get; init; }
    public int BlockZ { get; init; }
    public int Face { get; init; }
}

/// <summary>
/// UseItem embedded in AuthInput when <see cref="PlayerAuthInputPacket.InputFlagPerformItemInteraction"/> is set
/// (gophertunnel <c>PlayerInventoryAction</c>).
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
    public const int InputFlagPerformItemInteraction = 34;
    public const int InputFlagPerformBlockActions = 35;
    public const int InputFlagPerformItemStackRequest = 36;
    public const int InputFlagClientPredictedVehicle = 45;

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

        var inputData = ReadInputBitset(ref stream);
        _ = stream.ReadUnsignedVarInt(); // input_mode
        _ = stream.ReadUnsignedVarInt(); // play_mode
        _ = stream.ReadUnsignedVarInt(); // interaction_model
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // interact_pitch
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // interact_yaw
        _ = stream.ReadUnsignedVarLong(); // tick
        _ = stream.ReadFloat(BinaryStream.Endianess.Little); // delta x
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadFloat(BinaryStream.Endianess.Little);

        if (InputBitsetTest(inputData, InputFlagPerformItemInteraction))
            ItemInteraction = ReadUseItemTransactionData(ref stream);
        else
            ItemInteraction = null;

        if (InputBitsetTest(inputData, InputFlagPerformItemStackRequest))
            ItemStackRequestPacket.SkipEmbeddedRequest(ref stream);

        if (InputBitsetTest(inputData, InputFlagPerformBlockActions))
            BlockActions = ReadBlockActions(ref stream);
        else
            BlockActions = [];

        if (InputBitsetTest(inputData, InputFlagClientPredictedVehicle))
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

    /// <summary>Vedrock/gophertunnel: 7 data bits per byte, continuation bit 0x80.</summary>
    internal static byte[] ReadInputBitset(ref BinaryStream stream)
    {
        var bytes = new List<byte>(16);
        while (true)
        {
            var b = stream.ReadByte();
            bytes.Add(b);
            if ((b & 0x80) == 0) break;
            if (bytes.Count > 16) break; // hard cap (~112 flags)
        }

        return bytes.ToArray();
    }

    internal static bool InputBitsetTest(ReadOnlySpan<byte> bitset, int flag)
    {
        var idx = flag / 7;
        var pos = flag % 7;
        if (idx >= bitset.Length) return false;
        return (bitset[idx] & (1 << pos)) != 0;
    }

    /// <summary>gophertunnel Reader.PlayerInventoryAction — AuthInput item-interaction branch.</summary>
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

    /// <summary>gophertunnel ItemInstance (VarInt network id) — AuthInput item interaction.</summary>
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
