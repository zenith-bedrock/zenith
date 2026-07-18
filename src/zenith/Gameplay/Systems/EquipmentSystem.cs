using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Replica item na mão aos peers quando hotbar selecionada ou stack held muda.
/// Handler só escreve <see cref="Player.SelectedHotbarSlot"/>; fan-out neste tick.
/// </summary>
sealed class EquipmentSystem : IGameSystem
{
    private readonly PlayerManager _players;

    public EquipmentSystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock) => Tick(clock, _players.Online);

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

            if (slot == player.LastReplicatedHotbarSlot &&
                heldId == player.LastReplicatedHeldStackId &&
                count == player.LastReplicatedHeldCount)
                continue;

            player.LastReplicatedHotbarSlot = slot;
            player.LastReplicatedHeldStackId = heldId;
            player.LastReplicatedHeldCount = count;

            if (online.Count < 2) continue;

            var wire = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, slot);
            foreach (var peer in online)
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
