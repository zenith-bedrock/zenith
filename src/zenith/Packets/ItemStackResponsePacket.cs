using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct StackResponseSlotInfo
{
    public byte Slot { get; init; }
    public byte HotbarSlot { get; init; }
    public byte Count { get; init; }
    public int StackNetworkId { get; init; }

    /// <summary>
    /// item_stack_id is a genuine optional (ADR §90): a decorative presence bool followed by
    /// the option's own bool, then the zigzag32 payload if present — Zenith previously wrote a
    /// bare unconditional varint with neither marker.
    /// </summary>
    public void Write(ref BinaryStream writer)
    {
        writer.WriteByte(Slot);
        writer.WriteByte(HotbarSlot);
        writer.WriteByte(Count);
        var hasStackId = StackNetworkId != 0;
        writer.WriteBool(hasStackId); // item_stack_id_presence (decorative)
        writer.WriteBool(hasStackId); // item_stack_id option
        if (hasStackId)
            writer.WriteVarInt(StackNetworkId);
        writer.WriteVarString(""); // custom_name
        writer.WriteVarString(""); // filtered_custom_name
        writer.WriteVarInt(0); // durability_correction
    }
}

readonly struct StackResponseContainerInfo
{
    public FullContainerName Container { get; init; }
    public StackResponseSlotInfo[] SlotInfo { get; init; }

    public void Write(ref BinaryStream writer)
    {
        Container.Write(ref writer);
        writer.WriteUnsignedVarInt(SlotInfo.Length);
        foreach (var slot in SlotInfo)
            slot.Write(ref writer);
    }
}

readonly struct ItemStackResponseEntry
{
    public const byte StatusOk = 0;
    public const byte StatusError = 1;

    public byte Status { get; init; }
    public int RequestId { get; init; }
    public StackResponseContainerInfo[] ContainerInfo { get; init; }

    /// <summary>
    /// containers is unconditional in the struct (not gated by Status, ADR §90) — a decorative
    /// presence bool then the option's own bool, then the array if present. Zenith previously
    /// skipped both markers entirely on error and wrote a bare count on success.
    /// </summary>
    public void Write(ref BinaryStream writer)
    {
        writer.WriteByte(Status);
        writer.WriteVarInt(RequestId);
        var hasContainers = Status == StatusOk && ContainerInfo.Length > 0;
        writer.WriteBool(hasContainers); // containers_presence (decorative)
        writer.WriteBool(hasContainers); // containers option
        if (hasContainers)
        {
            writer.WriteUnsignedVarInt(ContainerInfo.Length);
            foreach (var ci in ContainerInfo)
                ci.Write(ref writer);
        }
    }
}

/// <summary>ItemStackResponse (0x94).</summary>
sealed class ItemStackResponsePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.ITEM_STACK_RESPONSE_PACKET;

    public ItemStackResponseEntry[] Responses { get; set; } = [];

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarInt(Responses.Length);
        foreach (var r in Responses)
            r.Write(ref writer);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }

    public static ItemStackResponsePacket Error(int requestId) => new()
    {
        Responses =
        [
            new ItemStackResponseEntry
            {
                Status = ItemStackResponseEntry.StatusError,
                RequestId = requestId,
                ContainerInfo = []
            }
        ]
    };

    public static ItemStackResponsePacket Ok(int requestId, StackResponseContainerInfo[] containers) => new()
    {
        Responses =
        [
            new ItemStackResponseEntry
            {
                Status = ItemStackResponseEntry.StatusOk,
                RequestId = requestId,
                ContainerInfo = containers
            }
        ]
    };
}
