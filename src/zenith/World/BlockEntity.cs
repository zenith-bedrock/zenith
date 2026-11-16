using Zenith.Player;

namespace Zenith.World;

/// <summary>
/// Server-side state of one placed block that does not fit in its runtime id: what a chest holds, how far a furnace has
/// cooked. Lives inside the <see cref="WorldColumn"/> of its cell, so it is created, persisted, evicted and reloaded with
/// the column — there is no separate store to keep in step (ADR §167).
/// </summary>
abstract class BlockEntity
{
    public abstract BlockEntityType Type { get; }
}

/// <summary>A kind of <see cref="BlockEntity"/>: how to make an empty one and how to turn one into bytes and back.</summary>
sealed class BlockEntityType
{
    private readonly Func<BlockEntity> _create;
    private readonly Func<BlockEntity, byte[]> _encode;
    private readonly Func<ReadOnlyMemory<byte>, BlockEntity?> _decode;

    public BlockEntityType(
        string id,
        Func<BlockEntity> create,
        Func<BlockEntity, byte[]> encode,
        Func<ReadOnlyMemory<byte>, BlockEntity?> decode)
    {
        Id = id;
        _create = create;
        _encode = encode;
        _decode = decode;
    }

    /// <summary>Stable name written to disk; never renumbered.</summary>
    public string Id { get; }

    public BlockEntity Create() => _create();

    public byte[] Encode(BlockEntity entity) => _encode(entity);

    /// <summary>Null when the bytes are not this type's encoding.</summary>
    public BlockEntity? Decode(ReadOnlyMemory<byte> bytes) => _decode(bytes);
}

/// <summary>A block that holds item slots (chest today; barrels, hoppers and furnaces build on it).</summary>
sealed class ContainerEntity : BlockEntity
{
    public const int SlotCount = 27;

    public InventorySlot[] Slots { get; }

    /// <summary>Runtime ids of the players that have this container open; never persisted.</summary>
    public HashSet<long> Openers { get; } = new();

    public ContainerEntity(InventorySlot[]? slots = null)
    {
        Slots = slots ?? CreateEmptySlots();
    }

    public override BlockEntityType Type => BlockEntityTypes.Chest;

    public bool IsEmpty
    {
        get
        {
            foreach (var slot in Slots)
                if (!slot.IsEmpty) return false;
            return true;
        }
    }

    private static InventorySlot[] CreateEmptySlots()
    {
        var slots = new InventorySlot[SlotCount];
        Array.Fill(slots, InventorySlot.Empty);
        return slots;
    }
}

/// <summary>The registry of block entity types; one place knows which blocks carry which.</summary>
static class BlockEntityTypes
{
    public static readonly BlockEntityType Chest = new(
        "chest",
        static () => new ContainerEntity(),
        static entity => SlotBlob.Pack(((ContainerEntity)entity).Slots),
        static bytes =>
        {
            var slots = new InventorySlot[ContainerEntity.SlotCount];
            return SlotBlob.TryUnpack(bytes.Span, slots) ? new ContainerEntity(slots) : null;
        });

    private static readonly BlockEntityType[] All = [Chest];

    public static bool TryGet(string id, out BlockEntityType type)
    {
        foreach (var candidate in All)
        {
            if (candidate.Id != id) continue;
            type = candidate;
            return true;
        }

        type = null!;
        return false;
    }

    /// <summary>The entity type a placed block needs, or null when its id says everything.</summary>
    public static BlockEntityType? ForBlock(int runtimeId) => Blocks.IsChest(runtimeId) ? Chest : null;
}
