using Zenith.Player;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Sequences the peer-visible side of an armor change (Phase XI.2) — same shape as
/// <see cref="ChestLidFanout"/>/<see cref="FloorDropFanout"/>: a small concrete helper, not a
/// GameSystem or an equipment framework.
/// </summary>
static class ArmorFanout
{
    public static void Broadcast(Player.Player subject, IReadOnlyList<Player.Player> online)
    {
        var inv = subject.Inventory;
        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Client.Entity.SendMobArmorEquipment(rid, inv);
        }
    }

    /// <summary>Peer-visible offhand: the same fan-out shape as <see cref="Broadcast"/>, for the one offhand slot.</summary>
    public static void BroadcastOffhand(Player.Player subject, IReadOnlyList<Player.Player> online)
    {
        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Client.Entity.SendMobOffhandEquipment(rid, subject.Inventory);
        }
    }
}
