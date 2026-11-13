using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Replica item na mão aos peers quando hotbar selecionada ou stack held muda.
/// Handler só escreve <see cref="Player.SelectedHotbarSlot"/>; fan-out neste tick.
/// </summary>
sealed class EquipmentSystem : IGameSystem
{
    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (!player.IsInGame) continue;

            var slot = player.SelectedHotbarSlot;
            if (!PlayerInventory.IsValidHotbarSlot(slot))
                slot = 0;

            var held = player.Inventory.Get(slot);
            var heldId = held.IsEmpty ? StackId.FromBlock(Blocks.Air) : held.Id;
            var count = held.IsEmpty ? 0 : held.Count;

            if (slot == player.Replicated.HotbarSlot &&
                heldId == player.Replicated.HeldStackId &&
                count == player.Replicated.HeldCount)
                continue;

            player.Replicated.HotbarSlot = slot;
            player.Replicated.HeldStackId = heldId;
            player.Replicated.HeldCount = count;

            if (online.Count < 2) continue;

            foreach (var peer in online)
            {
                if (ReferenceEquals(peer, player) || !peer.IsInGame) continue;
                peer.Client.Entity.SendMobEquipment(
                    (ulong)player.RuntimeId,
                    player.Inventory,
                    slot);
            }
        }
    }
}
