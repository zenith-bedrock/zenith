using Zenith.Gameplay.Ports;
using Zenith.Player;

namespace Zenith.Protocol;

/// <summary>
/// The parts of <see cref="IEntityChannel"/> that compose several wire sends (or read the recipient/subject sessions) so gameplay
/// can say one domain thing. They are the former <c>PlayerVisibility</c> private helpers and the per-species add wrappers.
/// </summary>
sealed partial class EntityProtocol
{
    public void SendMobEquipment(ulong actorRuntimeId, PlayerInventory inventory, int hotbarSlot) =>
        SendMobEquipment(actorRuntimeId, _session.Protocol.Inventory.DescribeSlot(inventory, hotbarSlot), hotbarSlot);

    public void SendMobArmorEquipment(ulong actorRuntimeId, PlayerInventory inventory)
    {
        var protocol = _session.Protocol.Inventory;
        SendMobArmorEquipment(
            actorRuntimeId,
            protocol.DescribeArmor(inventory, PlayerInventory.ArmorHelmetSlot),
            protocol.DescribeArmor(inventory, PlayerInventory.ArmorChestplateSlot),
            protocol.DescribeArmor(inventory, PlayerInventory.ArmorLeggingsSlot),
            protocol.DescribeArmor(inventory, PlayerInventory.ArmorBootsSlot));
    }

    public void SendMobOffhandEquipment(ulong actorRuntimeId, PlayerInventory inventory) =>
        SendMobOffhandEquipment(actorRuntimeId, _session.Protocol.Inventory.DescribeOffhand(inventory));

    public void SendPlayerListEntryOf(Player.Player subject)
    {
        var profile = subject.Session.Profile;
        SendPlayerListAdd(
            subject.Uuid,
            subject.RuntimeId,
            subject.Username,
            subject.Session.Skin,
            subject.SkinRgba,
            subject.SkinWidth,
            subject.SkinHeight,
            subject.Session.SkinTrusted,
            profile.Xuid,
            profile.PlatformChatId,
            profile.BuildPlatform);
    }

    public void SendPlayerActorOf(Player.Player subject)
    {
        var profile = subject.Session.Profile;
        var held = subject.Session.Protocol.Inventory.DescribeSlot(subject.Inventory, subject.SelectedHotbarSlot);
        SendAddPlayer(
            subject.Uuid,
            subject.Username,
            (ulong)subject.RuntimeId,
            subject.PositionX,
            subject.PositionY,
            subject.PositionZ,
            subject.Pitch,
            subject.Yaw,
            subject.HeadYaw,
            held,
            gameMode: (int)subject.GameMode,
            sneaking: subject.IsSneaking,
            sprinting: subject.IsSprinting,
            platformChatId: profile.PlatformChatId,
            deviceId: profile.DeviceId,
            buildPlatform: profile.BuildPlatform,
            usingItem: subject.FoodUse.HasValue);

        // PlayerList immediately preceding AddPlayer already carries the complete SerializedSkin.
        // Do not replay it as PlayerSkin while the recipient constructs the peer actor: PlayerSkin
        // is for a later skin change. Empty armour is implicit, but an already-equipped peer
        // needs one initial equipment projection before later armor deltas can apply.
        var inventory = subject.Inventory;
        var runtimeId = (ulong)subject.RuntimeId;
        SendHealth(runtimeId, subject.Health, subject.MaxHealth);
        if (!inventory.GetArmor(PlayerInventory.ArmorHelmetSlot).IsEmpty ||
            !inventory.GetArmor(PlayerInventory.ArmorChestplateSlot).IsEmpty ||
            !inventory.GetArmor(PlayerInventory.ArmorLeggingsSlot).IsEmpty ||
            !inventory.GetArmor(PlayerInventory.ArmorBootsSlot).IsEmpty)
            SendMobArmorEquipment(runtimeId, inventory);

        if (!inventory.Offhand.IsEmpty)
            SendMobOffhandEquipment(runtimeId, inventory);
    }
}
