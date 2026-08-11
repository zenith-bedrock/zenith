using Zenith.Player;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>
/// Per-session stack network ids for ISR (wire map SSOT:
/// <see cref="InventoryContainerMap"/>). Callers must not Refresh+Get ad hoc —
/// use <see cref="InventoryProtocol.DescribeForWire"/> / <see cref="InventoryProtocol.MatchesAdvertisedStackNetId"/>.
/// Keys are domain slot references rather than protocol-derived flat offsets.
/// </summary>
sealed class InventoryNetIds
{
    private readonly Dictionary<InventorySlotReference, int> _slotNetIds = new();
    private readonly Dictionary<InventorySlotReference, (StackId Id, int Count)> _stackIdentity = new();
    private int _nextNetId = 1;

    public int Allocate() => _nextNetId++;

    /// <summary>Last advertised id for <paramref name="reference"/> (no remint).</summary>
    public int Peek(in InventorySlotReference reference) =>
        _slotNetIds.GetValueOrDefault(reference);

    public void Set(in InventorySlotReference reference, int netId) =>
        _slotNetIds[reference] = netId;

    /// <summary>
    /// Remint when empty→air or (StackId, count) changes — Protocol-side identity until
    /// domain stacks own ids (DF-style). Deferred: NBT/damage identity.
    /// </summary>
    public int Refresh(in InventorySlotReference reference, InventorySlot slot)
    {
        if (slot.IsEmpty)
        {
            Set(reference, 0);
            _stackIdentity.Remove(reference);
            return 0;
        }

        if (_stackIdentity.TryGetValue(reference, out var prev) &&
            prev.Id == slot.Id && prev.Count == slot.Count)
            return Peek(reference);

        var id = Allocate();
        Set(reference, id);
        _stackIdentity[reference] = (slot.Id, slot.Count);
        return id;
    }

    /// <summary>
    /// Drops wire identity for the per-player open-container namespace when its authoritative
    /// session changes. Player/cursor/craft references deliberately retain their own identities.
    /// </summary>
    public void ClearOpenContainer()
    {
        var stale = new List<InventorySlotReference>();
        foreach (var reference in _slotNetIds.Keys)
        {
            if (reference.Area == InventorySlotArea.OpenContainer)
                stale.Add(reference);
        }

        foreach (var reference in stale)
        {
            _slotNetIds.Remove(reference);
            _stackIdentity.Remove(reference);
        }
    }
}
