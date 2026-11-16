namespace Zenith.World;

/// <summary>
/// Cells that want to be re-evaluated at a later game tick (ADR §164): a min-heap by due tick, one entry per cell
/// (the earliest request wins). This is the delay mechanism liquids and fire need; today it only re-runs the support
/// rules. RAM only, and bounded — at <see cref="SoftCap"/> a new cell is refused.
/// </summary>
sealed class ScheduledTicks
{
    internal const int SoftCap = 8192;

    private readonly PriorityQueue<(int X, int Y, int Z), ulong> _heap = new();
    private readonly Dictionary<(int X, int Y, int Z), ulong> _due = new();

    public int Count => _due.Count;

    public bool Schedule(int x, int y, int z, ulong dueTick)
    {
        var cell = (x, y, z);
        if (_due.TryGetValue(cell, out var existing))
        {
            if (existing <= dueTick) return true;
        }
        else if (_due.Count >= SoftCap)
        {
            return false;
        }

        _due[cell] = dueTick;
        _heap.Enqueue(cell, dueTick); // a superseded heap entry is skipped when it surfaces
        return true;
    }

    /// <summary>Pops one cell whose due tick has been reached.</summary>
    public bool TryPopDue(ulong nowTick, out int x, out int y, out int z)
    {
        while (_heap.TryPeek(out var cell, out var due))
        {
            if (due > nowTick) break;
            _heap.Dequeue();
            if (_due.TryGetValue(cell, out var current) && current == due)
            {
                _due.Remove(cell);
                (x, y, z) = cell;
                return true;
            }
        }

        x = y = z = 0;
        return false;
    }

    public void Clear()
    {
        _heap.Clear();
        _due.Clear();
    }
}
