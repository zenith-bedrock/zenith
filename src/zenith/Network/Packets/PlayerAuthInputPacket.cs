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
            SkipUseItemTransactionData(ref stream);

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

    private static void SkipUseItemTransactionData(ref BinaryStream stream)
    {
        _ = stream.ReadVarInt(); // action
        _ = stream.ReadByte(); // trigger
        _ = stream.ReadVarInt();
        _ = stream.ReadVarInt();
        _ = stream.ReadVarInt();
        _ = stream.ReadByte(); // face
        _ = stream.ReadVarInt(); // hotbar
        InventoryTransactionPacket.SkipNetworkItemPublic(ref stream);
        for (var i = 0; i < 6; i++)
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        _ = stream.ReadUnsignedVarInt();
        _ = stream.ReadByte();
        _ = stream.ReadByte();
    }
}
