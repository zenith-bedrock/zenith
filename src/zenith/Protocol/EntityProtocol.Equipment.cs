using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>What an actor holds and wears, as peers see it.</summary>
sealed partial class EntityProtocol
{
    public void SendMobEquipment(ulong actorRuntimeId, NetworkItemStack item, int hotbarSlot)
    {
        _session.SendDataPacket(new MobEquipmentPacket
        {
            ActorRuntimeId = (long)actorRuntimeId,
            Item = item,
            InventorySlot = hotbarSlot,
            HotbarSlot = hotbarSlot,
            WindowId = MobEquipmentPacket.WindowInventory
        });
    }

    /// <summary>Peer-visible offhand: MobEquipment on the legacy offhand window (slot 0 of it).</summary>
    public void SendMobOffhandEquipment(ulong actorRuntimeId, NetworkItemStack item)
    {
        _session.SendDataPacket(new MobEquipmentPacket
        {
            ActorRuntimeId = (long)actorRuntimeId,
            Item = item,
            InventorySlot = 0,
            HotbarSlot = 0,
            WindowId = MobEquipmentPacket.WindowOffhand
        });
    }

    /// <summary>Peer-visible armor (Phase XI.2) — head/torso/legs/feet; body slot unused by players.</summary>
    public void SendMobArmorEquipment(
        ulong actorRuntimeId, NetworkItemStack head, NetworkItemStack torso, NetworkItemStack legs, NetworkItemStack feet)
    {
        _session.SendDataPacket(new MobArmorEquipmentPacket
        {
            ActorRuntimeId = actorRuntimeId,
            Head = head,
            Torso = torso,
            Legs = legs,
            Feet = feet,
            Body = NetworkItemStack.Empty
        });
    }
}
