using Zenith.Player;

namespace Zenith.Gameplay;

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
        var protocol = subject.Session.Protocol.Inventory;
        var head = protocol.DescribeArmor(inv, PlayerInventory.ArmorHelmetSlot);
        var torso = protocol.DescribeArmor(inv, PlayerInventory.ArmorChestplateSlot);
        var legs = protocol.DescribeArmor(inv, PlayerInventory.ArmorLeggingsSlot);
        var feet = protocol.DescribeArmor(inv, PlayerInventory.ArmorBootsSlot);

        var rid = (ulong)subject.RuntimeId;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, subject)) continue;
            peer.Session.Protocol.Entity.SendMobArmorEquipment(rid, head, torso, legs, feet);
        }
    }
}
