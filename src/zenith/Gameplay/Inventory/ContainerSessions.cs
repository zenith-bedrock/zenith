using Zenith.Gameplay.Ports;
using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Gameplay.Inventory;

/// <summary>Opening and closing the container a player is looking at (inventory, chest, crafting table) and telling the client.</summary>
sealed class ContainerSessions
{
    private readonly World.World _world;

    public ContainerSessions(World.World world) => _world = world;

    public void Apply(
        global::Zenith.Player.Player player,
        in InventoryWindowIntent intent,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var inv = player.Client.Inventory;
        switch (intent.Action)
        {
            case InventoryWindowIntent.Kind.OpenInventory:
                // Bedrock may retransmit Interact(OpenInventory) while awaiting the first
                // ContainerOpen. Re-emitting ContainerOpen for an already-open player view can
                // crash the client (Dragonfly's handler_interact documents this behavior).
                // The open-container state is gameplay-owned, so coalesce it here rather than
                // making the network handler a second writer.
                var inventoryAlreadyOpen = player.Windows.InventoryWindowOpen;
                if (inventoryAlreadyOpen && player.Windows.OpenContainer?.Generation == intent.Generation)
                    return;

                if (player.Windows.OpenChest.HasValue)
                    ChestLidFanout.ReleaseOpener(online, _world, player);
                var inventorySession = player.Windows.OpenPlayerContainer(
                    (byte)InventoryContainerMap.WindowInventory,
                    InventoryContainerMap.WindowTypeInventory, intent.Generation);
                inv.BeginOpenContainerSession(inventorySession.Generation);
                if (!inventoryAlreadyOpen)
                    inv.SendContainerOpen(
                        (int)MathF.Floor(player.PositionX),
                        (int)MathF.Floor(player.PositionY),
                        (int)MathF.Floor(player.PositionZ));
                inv.SendUiInventoryContent(player);
                break;

            case InventoryWindowIntent.Kind.OpenChest:
                var view = ChestPairing.ViewFor(_world, intent.X, intent.Y, intent.Z);
                // A chest block whose storage cell does not exist yet gets one here.
                _world.Chests.Ensure(view.PrimaryX, view.PrimaryY, view.PrimaryZ);
                var partnerX = 0;
                var partnerY = 0;
                var partnerZ = 0;
                if (view.TryGetPartner(out partnerX, out partnerY, out partnerZ))
                    _world.Chests.Ensure(partnerX, partnerY, partnerZ);

                if (player.Windows.OpenChest is { } previous &&
                    !previous.Contains(intent.X, intent.Y, intent.Z))
                    ChestLidFanout.ReleaseOpener(online, _world, player);

                var chestSession = player.Windows.OpenChestContainer(
                    (byte)InventoryContainerMap.WindowChest,
                    InventoryContainerMap.WindowTypeChest,
                    view, intent.Generation);
                inv.BeginOpenContainerSession(chestSession.Generation);

                var primaryFirst = _world.Chests.TryAddOpener(
                    view.PrimaryX, view.PrimaryY, view.PrimaryZ, player.RuntimeId);
                var partnerFirst = false;
                if (view.TryGetPartner(out partnerX, out partnerY, out partnerZ))
                    partnerFirst = _world.Chests.TryAddOpener(partnerX, partnerY, partnerZ, player.RuntimeId);

                if (primaryFirst)
                    ChestLidFanout.Open(online, player.Client, view.PrimaryX, view.PrimaryY, view.PrimaryZ);
                if (partnerFirst)
                    ChestLidFanout.Open(online, player.Client, partnerX, partnerY, partnerZ);

                inv.SendChestOpen(intent.X, intent.Y, intent.Z);
                inv.SendChestContent(_world.Chests, view);
                inv.SendInventoryContent(player.Inventory);
                player.Level.Logger?.Debug(
                    $"Chest open for {player.Username} @ {intent.X},{intent.Y},{intent.Z} slots={view.SlotCount}");
                break;

            case InventoryWindowIntent.Kind.OpenCraftingTable:
                // Same re-transmit-crash risk OpenInventory's dedup guard above exists for.
                var tableAlreadyOpen = player.Windows.IsAtCraftingTable;
                if (tableAlreadyOpen && player.Windows.OpenContainer?.Generation == intent.Generation)
                    return;

                if (player.Windows.OpenChest.HasValue)
                    ChestLidFanout.ReleaseOpener(online, _world, player);

                var tableSession = player.Windows.OpenCraftingTableContainer(
                    (byte)InventoryContainerMap.WindowCraftingTable,
                    InventoryContainerMap.WindowTypeWorkbench, intent.Generation);
                inv.BeginOpenContainerSession(tableSession.Generation);
                if (!tableAlreadyOpen)
                    inv.SendCraftingTableOpen(intent.X, intent.Y, intent.Z);
                inv.SendUiInventoryContent(player);
                break;

            case InventoryWindowIntent.Kind.Close:
                // Phase XXIII-B fix: real Bedrock clients (documented in PocketMine's
                // InventoryManager::onClientRemoveWindow, "since 1.21.100 and probably earlier")
                // sometimes send WindowId 0xFF ("none", signed -1) instead of echoing the actual
                // window id — e.g. if another UI was already open when the close fired. Zenith's
                // exact-match check silently rejected that as "stale" and never cleared
                // player.Windows.OpenContainer, permanently locking the player out of every future
                // container open (found via real-client testing). Treat 0xFF as "close whatever is
                // currently open", the same workaround PocketMine uses.
                if (player.Windows.OpenContainer is not { } active)
                {
                    player.Level.Logger?.Debug(
                        $"Ignored stale container close from {player.Username}: {intent.WindowId}/{intent.WindowType}.");
                    return;
                }
                // Phase XXVI fix: WindowType ("ContainerType" on the wire) is NOT validated here —
                // confirmed against both PocketMine (onClientRemoveWindow only ever reasons about
                // windowId, the packet's ContainerType field is never even read) and Dragonfly
                // (ContainerCloseHandler switches purely on WindowID; ContainerType is only echoed
                // back in the ack, never compared). A real client was observed sending a stale/
                // unrelated ContainerType (247) on an otherwise-correct close (WindowId matched the
                // open chest exactly) — the previous WindowType comparison rejected that as
                // "mismatched", leaving the session open server-side and producing a rapid-fire
                // reopen loop client-side as it kept retrying. gophertunnel's own doc comment on this
                // field confirms it exists to validate a *server-initiated* close on the client side,
                // not something the server should validate on an incoming client-initiated close.
                if (active.Generation != intent.Generation ||
                    (intent.WindowId != InventoryWindowIntent.UnknownWindowId && active.WindowId != intent.WindowId))
                {
                    player.Level.Logger?.Debug(
                        $"Ignored mismatched container close from {player.Username}: {intent.WindowId}/{intent.WindowType} (active {active.WindowId}/{active.WindowType}).");
                    return;
                }

                if (active.Target == OpenContainerSession.TargetKind.Chest)
                    ChestLidFanout.ReleaseOpener(online, _world, player);
                else
                    _ = player.Windows.TryCloseContainer(active.WindowId, active.WindowType, out _);
                inv.EndOpenContainerSession();
                inv.SendContainerClose(active.WindowId, active.WindowType);
                break;
        }
    }
}
