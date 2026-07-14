using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Replica item na mão aos peers quando hotbar selecionada ou stack held muda.
/// Handler só escreve <see cref="Player.SelectedHotbarSlot"/>; fan-out neste tick.
/// </summary>
sealed class EquipmentSystem : IGameSystem
{
    private readonly PlayerManager _players;

    public EquipmentSystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            if (!player.IsInGame) continue;

            var slot = player.SelectedHotbarSlot;
            if (!PlayerInventory.IsValidHotbarSlot(slot))
                slot = 0;

            var held = player.Inventory.Get(slot);
            var runtimeId = held.IsEmpty ? 0 : held.RuntimeId;
            var count = held.IsEmpty ? 0 : held.Count;

            if (slot == player.LastReplicatedHotbarSlot &&
                runtimeId == player.LastReplicatedHeldRuntimeId &&
                count == player.LastReplicatedHeldCount)
                continue;

            player.LastReplicatedHotbarSlot = slot;
            player.LastReplicatedHeldRuntimeId = runtimeId;
            player.LastReplicatedHeldCount = count;

            if (_players.Count < 2) continue;

            var wire = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, slot);
            foreach (var peer in _players.Online)
            {
                if (ReferenceEquals(peer, player) || !peer.IsInGame) continue;
                peer.Session.Protocol.Entity.SendMobEquipment(
                    (ulong)player.RuntimeId,
                    wire,
                    slot);
            }
        }
    }
}
