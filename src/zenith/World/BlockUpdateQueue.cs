namespace Zenith.World;

/// <summary>
/// Cells whose block just changed and whose surroundings must be re-evaluated (ADR §164). Fed by every successful
/// world write; drained by the gameplay tick, which fans out to the six neighbours — so one edit costs O(1) when it is
/// written. Bounded and deduplicated: a cell already waiting is not queued twice, and at <see cref="SoftCap"/> new
/// cells are refused (the cell keeps its block; only the reaction is lost, and it is logged once by the caller).
/// RAM only — a reaction lost to a restart is recomputed by the next edit next to it.
/// </summary>
sealed class BlockUpdateQueue
{
    internal const int SoftCap = 8192;

    private readonly HashSet<(int X, int Y, int Z)> _waiting = new();
    private readonly Queue<(int X, int Y, int Z)> _order = new();

    public int Count => _waiting.Count;

    /// <summary>True when the cell is queued (or already was); false when the queue is at its cap.</summary>
    public bool Enqueue(int x, int y, int z)
    {
        var cell = (x, y, z);
        if (_waiting.Contains(cell)) return true;
        if (_waiting.Count >= SoftCap) return false;
        _waiting.Add(cell);
        _order.Enqueue(cell);
        return true;
    }

    public bool TryDequeue(out int x, out int y, out int z)
    {
        if (_order.Count > 0)
        {
            var cell = _order.Dequeue();
            _waiting.Remove(cell);
            (x, y, z) = cell;
            return true;
        }

        x = y = z = 0;
        return false;
    }

    public void Clear()
    {
        _waiting.Clear();
        _order.Clear();
    }
}
