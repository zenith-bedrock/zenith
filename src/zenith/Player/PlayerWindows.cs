using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// One player's inventory windows: the single authoritative open container (and the crafting grids that only exist while one is
/// open), the window intents the network hands over, and the ledger of already-claimed inventory request ids.
/// </summary>
sealed class PlayerWindows(Func<bool> isDead)
{
    public const int MaxPendingWindowIntents = 4;
    private const int ClaimedRequestMemory = 256;

    // The GameLoop is the only writer. This lock is a narrow snapshot handoff for the network
    // decoder, which must bind a wire container action to the session it observed.
    private readonly Lock _containerLock = new();
    private OpenContainerSession? _open;
    private uint _nextGeneration;
    private readonly PendingMailbox<InventoryWindowIntent> _intents = new(MaxPendingWindowIntents);

    // GameLoop-owned replay ledger: request IDs are protocol correlation tokens, not item authority.
    private readonly HashSet<int> _claimedRequests = new();
    private readonly Queue<int> _claimedRequestOrder = new();

    /// <summary>Ephemeral 2x2 craft grid, not persisted.</summary>
    public PlayerCraftUi CraftUi { get; } = new();

    /// <summary>Ephemeral 3x3 crafting-table grid (ADR §139), not persisted, only live while a crafting-table container is open.</summary>
    public PlayerTableCraftUi TableCraftUi { get; } = new();

    /// <summary>Single authoritative active container for this connection; assigned on GameLoop.</summary>
    public OpenContainerSession? OpenContainer
    {
        get
        {
            lock (_containerLock)
                return _open;
        }
    }

    /// <summary>
    /// Takes a coherent protocol-to-domain container-session snapshot at the network handoff.
    /// It grants no mutation authority; the InventorySystem revalidates its generation on tick.
    /// </summary>
    public bool TryGetOpenContainerSession(out OpenContainerSession session)
    {
        lock (_containerLock)
        {
            if (_open is not { } active)
            {
                session = default;
                return false;
            }

            session = active;
            return true;
        }
    }

    /// <summary>The chest (or double chest) the open container looks into; null when no container is open or it is another kind.</summary>
    public OpenChestView? OpenChest => OpenContainer is { Target: OpenContainerSession.TargetKind.Chest, Chest: { } chest } ? chest : null;

    /// <summary>Whether the player's own inventory window is the open container.</summary>
    public bool InventoryWindowOpen => OpenContainer is { Target: OpenContainerSession.TargetKind.PlayerInventory };

    /// <summary>Whether a crafting-table window is the currently active container (ADR §139).</summary>
    public bool IsAtCraftingTable => OpenContainer is { Target: OpenContainerSession.TargetKind.CraftingTable };

    public OpenContainerSession OpenPlayerContainer(byte windowId, byte windowType) =>
        Open(generation => OpenContainerSession.PlayerInventory(windowId, windowType, generation));

    public OpenContainerSession OpenChestContainer(byte windowId, byte windowType, in OpenChestView view)
    {
        var chest = view;
        return Open(generation => OpenContainerSession.ChestView(windowId, windowType, generation, chest));
    }

    public OpenContainerSession OpenCraftingTableContainer(byte windowId, byte windowType) =>
        Open(generation => OpenContainerSession.CraftingTable(windowId, windowType, generation));

    private OpenContainerSession Open(Func<uint, OpenContainerSession> create)
    {
        lock (_containerLock)
        {
            var session = create(++_nextGeneration);
            _open = session;
            return session;
        }
    }

    public bool TryCloseContainer(byte windowId, byte windowType, out OpenContainerSession closed)
    {
        lock (_containerLock)
        {
            if (_open is not { } active || active.WindowId != windowId || active.WindowType != windowType)
            {
                closed = default;
                return false;
            }

            _open = null;
            closed = active;
            return true;
        }
    }

    public bool TryClearOpenContainer(out OpenContainerSession closed)
    {
        lock (_containerLock)
        {
            if (_open is not { } active)
            {
                closed = default;
                return false;
            }

            _open = null;
            closed = active;
            return true;
        }
    }

    public bool SubmitWindowIntent(in InventoryWindowIntent intent)
    {
        if (isDead() && intent.Action != InventoryWindowIntent.Kind.Close) return false;
        return _intents.Submit(intent);
    }

    public bool TryConsumeWindowIntent(out InventoryWindowIntent intent) => _intents.TryConsume(out intent);

    /// <summary>
    /// Reports whether the latest not-yet-applied window intent will open a compatible container. Session uses this only to retain an
    /// ISR that arrived immediately after the corresponding open packet; <see cref="Gameplay.Inventory.InventorySystem"/> remains the
    /// sole owner that creates and validates the actual container session.
    /// </summary>
    public bool HasPendingContainerOpen(OpenContainerSession.TargetKind? expectedTarget)
    {
        if (!_intents.TryPeekLast(out var pending))
            return false;

        return pending.Action switch
        {
            InventoryWindowIntent.Kind.OpenInventory => expectedTarget is null or OpenContainerSession.TargetKind.PlayerInventory,
            InventoryWindowIntent.Kind.OpenChest => expectedTarget is null or OpenContainerSession.TargetKind.Chest,
            _ => false
        };
    }

    /// <summary>
    /// Claims one ISR request id exactly once before any authoritative mutation. Kept bounded so a long-lived session cannot turn
    /// replay protection into unbounded memory.
    /// </summary>
    public bool TryClaimInventoryRequest(int requestId)
    {
        if (!_claimedRequests.Add(requestId))
            return false;

        _claimedRequestOrder.Enqueue(requestId);
        if (_claimedRequestOrder.Count > ClaimedRequestMemory)
            _claimedRequests.Remove(_claimedRequestOrder.Dequeue());
        return true;
    }
}
