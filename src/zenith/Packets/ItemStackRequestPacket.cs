using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct FullContainerName
{
    public byte ContainerId { get; init; }
    public uint? DynamicId { get; init; }

    public static FullContainerName Read(ref BinaryStream stream)
    {
        var id = stream.ReadByte();
        uint? dynamic = null;
        if (stream.ReadBool())
            dynamic = stream.ReadUInt(BinaryStream.Endianess.Little);
        return new FullContainerName { ContainerId = id, DynamicId = dynamic };
    }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteByte(ContainerId);
        if (DynamicId is { } d)
        {
            writer.WriteBool(true);
            writer.WriteUInt(d, BinaryStream.Endianess.Little);
        }
        else
            writer.WriteBool(false);
    }
}

readonly struct StackRequestSlotInfo
{
    public FullContainerName Container { get; init; }
    public byte Slot { get; init; }
    public int StackNetworkId { get; init; }

    public static StackRequestSlotInfo Read(ref BinaryStream stream) => new()
    {
        Container = FullContainerName.Read(ref stream),
        Slot = stream.ReadByte(),
        StackNetworkId = stream.ReadVarInt()
    };
}

readonly struct DecodedStackRequestAction
{
    public byte ActionType { get; init; }
    public byte Count { get; init; }
    public StackRequestSlotInfo Source { get; init; }
    public StackRequestSlotInfo Destination { get; init; }
    public uint RecipeNetId { get; init; }
    public uint CreativeNetId { get; init; }
    public byte CraftTimes { get; init; }
    public bool Supported { get; init; }
}

readonly struct DecodedItemStackRequest
{
    public int RequestId { get; init; }
    public DecodedStackRequestAction[] Actions { get; init; }
    public bool AllSupported { get; init; }
}

/// <summary>
/// ItemStackRequest (0x93) — decode take/place/swap/container + CraftRecipe/CraftCreative;
/// Consume/Create are supported no-ops so craft UI requests are not rejected wholesale.
/// CraftResultsDeprecated skipado como supported (no-op); restantes unsupported.
/// </summary>
sealed class ItemStackRequestPacket : DataPacket
{
    public const byte ActionTake = 0;
    public const byte ActionPlace = 1;
    public const byte ActionSwap = 2;
    public const byte ActionDrop = 3;
    public const byte ActionDestroy = 4;
    public const byte ActionConsume = 5;
    public const byte ActionCreate = 6;
    public const byte ActionPlaceInContainer = 7;
    public const byte ActionTakeOutContainer = 8;
    public const byte ActionLabTableCombine = 9;
    public const byte ActionBeaconPayment = 10;
    public const byte ActionMineBlock = 11;
    public const byte ActionCraftRecipe = 12;
    public const byte ActionCraftRecipeAuto = 13;
    public const byte ActionCraftCreative = 14;
    public const byte ActionCraftRecipeOptional = 15;
    public const byte ActionCraftGrindstone = 16;
    public const byte ActionCraftLoom = 17;
    public const byte ActionCraftNonImplemented = 18;
    public const byte ActionCraftResultsDeprecated = 19;

    public override int Id => (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET;

    public DecodedItemStackRequest[] Requests { get; set; } = [];

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        var count = stream.ReadUnsignedVarInt();
        var list = new DecodedItemStackRequest[count];
        for (var i = 0; i < count; i++)
            list[i] = ReadEntry(ref stream);
        Requests = list;
    }

    /// <summary>Single embedded request inside PlayerAuthInput (no outer request count).</summary>
    internal static void SkipEmbeddedRequest(ref BinaryStream stream) => ReadEntry(ref stream);

    private static DecodedItemStackRequest ReadEntry(ref BinaryStream stream)
    {
        var requestId = stream.ReadVarInt();
        var actionCount = stream.ReadUnsignedVarInt();
        var actions = new DecodedStackRequestAction[actionCount];
        var allSupported = true;
        for (var i = 0; i < actionCount; i++)
        {
            actions[i] = ReadAction(ref stream);
            if (!actions[i].Supported)
                allSupported = false;
        }

        var filterCount = stream.ReadUnsignedVarInt();
        for (var i = 0; i < filterCount; i++)
            stream.ReadVarString();
        stream.ReadInt(BinaryStream.Endianess.Little); // filter cause

        return new DecodedItemStackRequest
        {
            RequestId = requestId,
            Actions = actions,
            AllSupported = allSupported
        };
    }

    private static DecodedStackRequestAction ReadAction(ref BinaryStream stream)
    {
        var type = stream.ReadByte();
        switch (type)
        {
            case ActionTake:
            case ActionPlace:
            {
                var count = stream.ReadByte();
                var src = StackRequestSlotInfo.Read(ref stream);
                var dst = StackRequestSlotInfo.Read(ref stream);
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Count = count,
                    Source = src,
                    Destination = dst,
                    Supported = true
                };
            }
            case ActionPlaceInContainer:
            case ActionTakeOutContainer:
            {
                var count = stream.ReadByte();
                var src = StackRequestSlotInfo.Read(ref stream);
                var dst = StackRequestSlotInfo.Read(ref stream);
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Count = count,
                    Source = src,
                    Destination = dst,
                    Supported = true
                };
            }
            case ActionSwap:
            {
                var src = StackRequestSlotInfo.Read(ref stream);
                var dst = StackRequestSlotInfo.Read(ref stream);
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Source = src,
                    Destination = dst,
                    Supported = true
                };
            }
            case ActionDrop:
            {
                var count = stream.ReadByte();
                var src = StackRequestSlotInfo.Read(ref stream);
                _ = stream.ReadBool();
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Count = count,
                    Source = src,
                    Supported = true
                };
            }
            case ActionDestroy:
                stream.ReadByte();
                _ = StackRequestSlotInfo.Read(ref stream);
                return Unsupported(type);
            case ActionConsume:
            {
                // Consume already applied in the domain craft tick — acknowledge and skip payload.
                stream.ReadByte();
                _ = StackRequestSlotInfo.Read(ref stream);
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Supported = true
                };
            }
            case ActionCreate:
                // Output materialize is domain TryCraft — Create is wire ack only (no pendingResults).
                stream.ReadByte();
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Supported = true
                };
            case ActionLabTableCombine:
                return Unsupported(type);
            case ActionBeaconPayment:
                stream.ReadVarInt();
                stream.ReadVarInt();
                return Unsupported(type);
            case ActionMineBlock:
                stream.ReadVarInt();
                stream.ReadVarInt();
                stream.ReadVarInt();
                return Unsupported(type);
            case ActionCraftRecipe:
            {
                var recipeNetId = (uint)stream.ReadUnsignedVarInt();
                var times = stream.ReadByte();
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    RecipeNetId = recipeNetId,
                    CraftTimes = times,
                    Supported = true
                };
            }
            case ActionCraftRecipeAuto:
            {
                stream.ReadUnsignedVarInt();
                stream.ReadByte();
                stream.ReadByte();
                var ing = stream.ReadUnsignedVarInt();
                for (var i = 0; i < ing; i++)
                    SkipItemDescriptorCount(ref stream);
                return Unsupported(type);
            }
            case ActionCraftCreative:
            {
                var creativeNetId = (uint)stream.ReadUnsignedVarInt();
                var times = stream.ReadByte();
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    CreativeNetId = creativeNetId,
                    CraftTimes = times,
                    Supported = true
                };
            }
            case ActionCraftRecipeOptional:
                stream.ReadUnsignedVarInt();
                stream.ReadInt(BinaryStream.Endianess.Little);
                return Unsupported(type);
            case ActionCraftGrindstone:
                stream.ReadUnsignedVarInt();
                stream.ReadByte();
                stream.ReadVarInt();
                return Unsupported(type);
            case ActionCraftLoom:
                stream.ReadVarString();
                stream.ReadByte();
                return Unsupported(type);
            case ActionCraftNonImplemented:
                return Unsupported(type);
            case ActionCraftResultsDeprecated:
            {
                var n = stream.ReadUnsignedVarInt();
                for (var i = 0; i < n; i++)
                    SkipItemStackWithoutNetId(ref stream);
                stream.ReadByte();
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Supported = true
                };
            }
            default:
                return Unsupported(type);
        }
    }

    private static DecodedStackRequestAction Unsupported(byte type) => new()
    {
        ActionType = type,
        Supported = false
    };

    private static void SkipItemDescriptorCount(ref BinaryStream stream)
    {
        var descriptorType = stream.ReadByte();
        switch (descriptorType)
        {
            case ItemDescriptorType.Invalid:
                break;
            case ItemDescriptorType.Default:
            {
                var networkId = stream.ReadShort(BinaryStream.Endianess.Little);
                if (networkId != 0)
                    stream.ReadShort(BinaryStream.Endianess.Little);
                break;
            }
            case ItemDescriptorType.Molang:
                stream.ReadVarString();
                stream.ReadByte();
                break;
            case ItemDescriptorType.ItemTag:
                stream.ReadVarString();
                break;
            case ItemDescriptorType.Deferred:
                stream.ReadVarString();
                stream.ReadShort(BinaryStream.Endianess.Little);
                break;
            case ItemDescriptorType.ComplexAlias:
                stream.ReadVarString();
                break;
        }

        stream.ReadVarInt(); // count
    }

    private static void SkipItemStackWithoutNetId(ref BinaryStream stream)
    {
        var id = stream.ReadVarInt();
        if (id == 0) return;
        stream.ReadUShort(BinaryStream.Endianess.Little);
        stream.ReadUnsignedVarInt();
        stream.ReadVarInt();
        var extra = stream.ReadUnsignedVarInt();
        if (extra > 0) stream.ReadSpan(extra);
    }
}
