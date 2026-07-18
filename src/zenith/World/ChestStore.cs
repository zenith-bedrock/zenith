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
    /// <summary>Open UI viewers per cell (runtime id) — lid BlockEvent on 0→1 / 1→0 (§28).</summary>
    private readonly Dictionary<(int X, int Y, int Z), HashSet<long>> _openers = new();
    private readonly ILogger? _logger;
    private int _thresholdWarned;

    public ChestStore(ILogger? logger = null) => _logger = logger;

    public int Count => _chests.Count;

    public int OpenerCount(int x, int y, int z) =>
        _openers.TryGetValue((x, y, z), out var set) ? set.Count : 0;

    /// <summary>Returns true when this was the first opener (0→1) — animate lid open.</summary>
    public bool TryAddOpener(int x, int y, int z, long playerRuntimeId)
    {
        var key = (x, y, z);
        if (!_openers.TryGetValue(key, out var set))
        {
            set = new HashSet<long>();
            _openers[key] = set;
        }

        if (!set.Add(playerRuntimeId))
            return false;
        return set.Count == 1;
    }

    /// <summary>Returns true when this was the last opener (1→0) — animate lid close.</summary>
    public bool TryRemoveOpener(int x, int y, int z, long playerRuntimeId)
    {
        var key = (x, y, z);
        if (!_openers.TryGetValue(key, out var set))
            return false;
        if (!set.Remove(playerRuntimeId))
            return false;
        if (set.Count > 0)
            return false;
        _openers.Remove(key);
        return true;
    }

    /// <summary>Clears all openers; returns true if the lid was open (count &gt; 0).</summary>
    public bool ClearOpeners(int x, int y, int z)
    {
        var key = (x, y, z);
        if (!_openers.Remove(key, out var set))
            return false;
        return set.Count > 0;
    }

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

    /// <summary>Remove o baú e devolve o conteúdo não-vazio como lista (StackId, count).</summary>
    public List<(StackId Id, int Count)> RemoveAndDump(int x, int y, int z)
    {
        ClearOpeners(x, y, z);
        var list = new List<(StackId, int)>();
        if (!_chests.Remove((x, y, z), out var slots))
            return list;

        foreach (var s in slots)
        {
            if (!s.IsEmpty)
                list.Add((s.Id, s.Count));
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
