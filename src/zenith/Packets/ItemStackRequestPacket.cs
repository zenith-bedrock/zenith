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

    /// <summary>StackId is a fixed li32 (not a varint) — confirmed against minecraft-data 1.26.40.</summary>
    public static StackRequestSlotInfo Read(ref BinaryStream stream) => new()
    {
        Container = FullContainerName.Read(ref stream),
        Slot = stream.ReadByte(),
        StackNetworkId = stream.ReadInt(BinaryStream.Endianess.Little)
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
    public byte ResultSlot { get; init; }
    public int HotbarSlot { get; init; }
    public int StackNetworkId { get; init; }
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
///
/// Action type numbers and per-action field widths follow protocol 2168. Each Cereal action has
/// its uvarint union variant followed by the legacy <c>uint8 Action type</c>; the two container
/// action IDs are omitted from the union, so variants at and after 7 map to legacy IDs +2. The
/// typed client request id remains a zigzag varint despite its underlying <c>int32_t</c> storage
/// type.
/// </summary>
sealed class ItemStackRequestPacket : DataPacket
{
    // Matches PocketMine's protective request cap. A vanilla craft request can legitimately
    // touch many slots, but never needs an unbounded client-controlled allocation.
    public const int MaxRequestsPerPacket = 60;
    public const int MaxActionsPerRequest = 60;
    public const int MaxFilterStringsPerRequest = 64;
    public const int MaxDeprecatedResults = 64;

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
        if (count > MaxRequestsPerPacket)
            throw new InvalidDataException($"ItemStackRequest has {count} requests (max {MaxRequestsPerPacket}).");
        var list = new DecodedItemStackRequest[count];
        for (var i = 0; i < count; i++)
            list[i] = ReadEntry(ref stream);
        Requests = list;
    }

    /// <summary>Single embedded request inside PlayerAuthInput (no outer request count).</summary>
    internal static DecodedItemStackRequest ReadEmbeddedRequest(ref BinaryStream stream) => ReadEntry(ref stream);

    private static DecodedItemStackRequest ReadEntry(ref BinaryStream stream)
    {
        var requestId = stream.ReadVarInt();
        var actionCount = stream.ReadUnsignedVarInt();
        if (actionCount > MaxActionsPerRequest)
            throw new InvalidDataException($"ItemStackRequest {requestId} has {actionCount} actions (max {MaxActionsPerRequest}).");
        var actions = new DecodedStackRequestAction[actionCount];
        var allSupported = true;
        for (var i = 0; i < actionCount; i++)
        {
            actions[i] = ReadAction(ref stream);
            if (!actions[i].Supported)
                allSupported = false;
        }

        // custom_names — filter strings the client wants applied (anvil/sign/book text etc.).
        var filterCount = stream.ReadUnsignedVarInt();
        if (filterCount > MaxFilterStringsPerRequest)
            throw new InvalidDataException($"ItemStackRequest {requestId} has {filterCount} filter strings (max {MaxFilterStringsPerRequest}).");
        for (var i = 0; i < filterCount; i++)
            stream.ReadVarString();
        stream.ReadInt(BinaryStream.Endianess.Little); // cause (mapper li32)

        return new DecodedItemStackRequest
        {
            RequestId = requestId,
            Actions = actions,
            AllSupported = allSupported
        };
    }

    private static DecodedStackRequestAction ReadAction(ref BinaryStream stream)
    {
        var variant = stream.ReadUnsignedVarInt();
        var type = stream.ReadByte();
        if (type != LegacyActionId(variant))
            throw new InvalidDataException($"ItemStackRequest action variant {variant} disagrees with legacy ID {type}.");

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
                _ = stream.ReadBool(); // randomly
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Count = count,
                    Source = src,
                    Supported = true
                };
            }
            case ActionDestroy:
            {
                var count = stream.ReadByte();
                var src = StackRequestSlotInfo.Read(ref stream);
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Count = count,
                    Source = src,
                    Supported = true
                };
            }
            case ActionConsume:
            {
                // Consume already applied in the domain craft tick — acknowledge and skip payload.
                var count = stream.ReadByte();
                var src = StackRequestSlotInfo.Read(ref stream);
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    Count = count,
                    Source = src,
                    Supported = true
                };
            }
            case ActionCreate:
                // Zenith presently has one authoritative pending result (CreatedOutput slot 0).
                // Retain the wire index so Session can reject a multi-result request instead of
                // acknowledging a result that the domain cannot represent.
                var resultSlot = stream.ReadByte();
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    ResultSlot = resultSlot,
                    Supported = true
                };
            case ActionPlaceInContainer:
            case ActionTakeOutContainer:
                // These legacy action IDs are omitted from the Cereal variant list and are not
                // emitted by the vanilla 2168 client. If received, reject the whole request.
                return Unsupported(type);
            case ActionLabTableCombine:
                return Unsupported(type); // void — no fields
            case ActionBeaconPayment:
                stream.ReadVarInt(); // primary_effect
                stream.ReadVarInt(); // secondary_effect
                return Unsupported(type);
            case ActionMineBlock:
                var hotbarSlot = stream.ReadVarInt();
                _ = stream.ReadVarInt(); // predicted durability: no authoritative durability state yet
                var stackNetworkId = stream.ReadInt(BinaryStream.Endianess.Little);
                return new DecodedStackRequestAction
                {
                    ActionType = type,
                    HotbarSlot = hotbarSlot,
                    StackNetworkId = stackNetworkId,
                    Supported = true
                };
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
                stream.ReadUnsignedVarInt(); // recipe_network_id
                stream.ReadByte(); // times_crafted
                var ing = stream.ReadUnsignedVarInt();
                for (var i = 0; i < ing; i++)
                    SkipRecipeIngredient(ref stream);
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
                stream.ReadUnsignedVarInt(); // recipe_network_id
                stream.ReadInt(BinaryStream.Endianess.Little); // filtered_string_index (li32)
                return Unsupported(type);
            case ActionCraftGrindstone:
                stream.ReadInt(BinaryStream.Endianess.Little); // recipe_network_id (li32)
                stream.ReadByte(); // times_crafted
                stream.ReadVarInt(); // cost (zigzag32)
                return Unsupported(type);
            case ActionCraftLoom:
                stream.ReadVarString(); // pattern
                stream.ReadByte(); // times_crafted
                return Unsupported(type);
            case ActionCraftNonImplemented:
                return Unsupported(type); // void
            case ActionCraftResultsDeprecated:
            {
                var n = stream.ReadUnsignedVarInt();
                if (n > MaxDeprecatedResults)
                    throw new InvalidDataException($"ItemStackRequest has {n} deprecated craft results (max {MaxDeprecatedResults}).");
                for (var i = 0; i < n; i++)
                    SkipInstanceDescriptor(ref stream);
                stream.ReadByte(); // times_crafted
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

    private static byte LegacyActionId(int variant)
    {
        if (variant > ActionCraftResultsDeprecated - 2)
            throw new InvalidDataException($"Unknown ItemStackRequest action variant {variant}.");
        return checked((byte)(variant < ActionPlaceInContainer ? variant : variant + 2));
    }

    private static DecodedStackRequestAction Unsupported(byte type) => new()
    {
        ActionType = type,
        Supported = false
    };

    /// <summary>
    /// RecipeIngredient2 (craft_recipe_auto's ingredient list): type(mapper varint) +
    /// legacy_type(u8) + switch{invalid:void, name:{name,metadata}, molang:{expr,version},
    /// item_tag:{tag}} + count(lu16).
    /// </summary>
    private static void SkipRecipeIngredient(ref BinaryStream stream)
    {
        var type = stream.ReadUnsignedVarInt();
        _ = stream.ReadByte(); // legacy_type
        switch (type)
        {
            case 0: // invalid — void
                break;
            case 1: // name
                stream.ReadVarString(); // name
                stream.ReadVarInt(); // metadata (zigzag32)
                break;
            case 2: // molang
                stream.ReadVarString(); // expression
                stream.ReadShort(BinaryStream.Endianess.Little); // version (li16)
                break;
            case 3: // item_tag
                stream.ReadVarString(); // tag
                break;
        }

        stream.ReadUShort(BinaryStream.Endianess.Little); // count (lu16)
    }

    /// <summary>
    /// ItemStackRequestInstanceDescriptor (results_deprecated entries): type(mapper varint) +
    /// legacy_type(u8) + switch{invalid:void, default:{name,metadata}} + count(li16) +
    /// block_runtime_id(varint) + extra(varint-length blob).
    /// </summary>
    private static void SkipInstanceDescriptor(ref BinaryStream stream)
    {
        var type = stream.ReadUnsignedVarInt();
        _ = stream.ReadByte(); // legacy_type
        if (type != 0) // not "invalid"
        {
            stream.ReadVarString(); // name
            stream.ReadVarInt(); // metadata (zigzag32)
        }

        stream.ReadShort(BinaryStream.Endianess.Little); // count (li16)
        stream.ReadUnsignedVarInt(); // block_runtime_id
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0)
            stream.ReadSpan(extraLen);
    }
}
