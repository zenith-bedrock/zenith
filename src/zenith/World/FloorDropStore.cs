namespace Zenith.World;

/// <summary>
/// Floor drops without Bedrock item entities (ADR §26): sparse cells holding a stack,
/// picked up when a player walks within 1.5 blocks on the GameLoop tick.
/// </summary>
sealed class FloorDropStore
{
    public const int MaxStack = 64;

    private readonly Dictionary<(int X, int Y, int Z), DropSlot> _drops = new();

    readonly record struct DropSlot(int RuntimeId, int Count);

    public int Count => _drops.Count;

    public void AddOrMerge(int x, int y, int z, int runtimeId, int count)
    {
        if (count <= 0 || runtimeId == Blocks.Air) return;
        var key = (x, y, z);
        if (_drops.TryGetValue(key, out var existing) && existing.RuntimeId == runtimeId)
        {
            var merged = Math.Min(MaxStack, existing.Count + count);
            _drops[key] = new DropSlot(runtimeId, merged);
            return;
        }

        _drops[key] = new DropSlot(runtimeId, Math.Min(MaxStack, count));
    }

    public IEnumerable<((int X, int Y, int Z) Pos, int RuntimeId, int Count)> Snapshot()
    {
        foreach (var (pos, slot) in _drops)
            yield return (pos, slot.RuntimeId, slot.Count);
    }

    public bool TryTake(int x, int y, int z, out int runtimeId, out int count)
    {
        if (!_drops.Remove((x, y, z), out var slot))
        {
            runtimeId = Blocks.Air;
            count = 0;
            return false;
        }

        runtimeId = slot.RuntimeId;
        count = slot.Count;
        return true;
    }
}
