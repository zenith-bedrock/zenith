using System.Threading;
using Zenith.Player;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// RAM chest inventories keyed by block position (ADR §28 / §36).
/// Persistência LevelDB fica fora deste MVP. Warn once when crossing Ensure threshold — no silent drop.
/// </summary>
sealed class ChestStore
{
    public const int Size = 27;
    internal const int WarnThreshold = 10_000;

    private readonly Dictionary<(int X, int Y, int Z), InventorySlot[]> _chests = new();
    private readonly ILogger? _logger;
    private int _thresholdWarned;

    public ChestStore(ILogger? logger = null) => _logger = logger;

    public int Count => _chests.Count;

    public void Ensure(int x, int y, int z)
    {
        var key = (x, y, z);
        if (_chests.ContainsKey(key)) return;
        var slots = new InventorySlot[Size];
        for (var i = 0; i < Size; i++)
            slots[i] = InventorySlot.Empty;
        _chests[key] = slots;

        if (_chests.Count >= WarnThreshold && Interlocked.Exchange(ref _thresholdWarned, 1) == 0)
        {
            _logger?.Warning(
                $"ChestStore crossed WarnThreshold ({WarnThreshold} chests in RAM). " +
                "No refuse/eviction — LevelDB persistence redesign needed for honesty at scale.");
        }
    }

    public bool TryGetSlots(int x, int y, int z, out InventorySlot[] slots) =>
        _chests.TryGetValue((x, y, z), out slots!);

    public InventorySlot Get(int x, int y, int z, int slot)
    {
        if ((uint)slot >= Size) return InventorySlot.Empty;
        if (!_chests.TryGetValue((x, y, z), out var slots)) return InventorySlot.Empty;
        return slots[slot];
    }

    public bool TrySet(int x, int y, int z, int slot, InventorySlot value)
    {
        if ((uint)slot >= Size) return false;
        if (!_chests.TryGetValue((x, y, z), out var slots)) return false;
        slots[slot] = value.IsEmpty ? InventorySlot.Empty : value;
        return true;
    }

    /// <summary>Remove o baú e devolve o conteúdo não-vazio como lista (runtimeId, count).</summary>
    public List<(int RuntimeId, int Count)> RemoveAndDump(int x, int y, int z)
    {
        var list = new List<(int, int)>();
        if (!_chests.Remove((x, y, z), out var slots))
            return list;

        foreach (var s in slots)
        {
            if (!s.IsEmpty)
                list.Add((s.RuntimeId, s.Count));
        }

        return list;
    }

    public InventorySlot[] CaptureSnapshot(int x, int y, int z)
    {
        if (!_chests.TryGetValue((x, y, z), out var slots))
            return Array.Empty<InventorySlot>();
        var copy = new InventorySlot[Size];
        Array.Copy(slots, copy, Size);
        return copy;
    }

    public void RestoreSnapshot(int x, int y, int z, InventorySlot[] snapshot)
    {
        if (snapshot.Length != Size) return;
        Ensure(x, y, z);
        Array.Copy(snapshot, _chests[(x, y, z)], Size);
    }

    public byte[]? PackBlob(int x, int y, int z)
    {
        if (!_chests.TryGetValue((x, y, z), out var slots))
            return null;
        return SlotBlob.Pack(slots);
    }

    public bool TryLoadFromBlob(int x, int y, int z, ReadOnlySpan<byte> data)
    {
        Span<InventorySlot> slots = stackalloc InventorySlot[Size];
        if (!SlotBlob.TryUnpack(data, slots))
            return false;
        Ensure(x, y, z);
        slots.CopyTo(_chests[(x, y, z)]);
        return true;
    }
}
