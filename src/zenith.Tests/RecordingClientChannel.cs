using Zenith.Gameplay.Ports;
using Zenith.Player;
using Zenith.Rulebook;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// A client that records what gameplay says to it, as calls on the ports (name + arguments), instead of packets. Attach it with
/// <see cref="Zenith.Player.Player.AttachClient"/>. Generated from <c>ClientPorts.cs</c>'s method list; a port member added there
/// fails the build here until it is recorded too.
/// </summary>
internal sealed class RecordingClientChannel : IClientChannel
{
    public List<(string Channel, string Call, object?[] Args)> Calls { get; } = [];

    public RecordingClientChannel()
    {
        Entity = new EntityRecorder(this);
        Inventory = new InventoryRecorder(this);
        World = new WorldRecorder(this);
        Chat = new ChatRecorder(this);
        Ui = new UiRecorder(this);
    }

    public IEntityChannel Entity { get; }
    public IInventoryChannel Inventory { get; }
    public IWorldChannel World { get; }
    public IChatChannel Chat { get; }
    public IUiChannel Ui { get; }
    public bool IsClosed => false;
    public string Xuid => "";

    public IEnumerable<object?[]> Of(string call) => Calls.Where(c => c.Call == call).Select(c => c.Args);
    public int Count(string call) => Calls.Count(c => c.Call == call);

    private sealed class EntityRecorder(RecordingClientChannel owner) : IEntityChannel
    {
        public void SendAddMob(MobKind kind, long entityUniqueId, ulong entityRuntimeId, float x, float y, float z, float yaw) => owner.Calls.Add(("Entity", "SendAddMob", [kind, entityUniqueId, entityRuntimeId, x, y, z, yaw]));
        public void SendAddProjectile(long entityUniqueId, ulong entityRuntimeId, float x, float y, float z, float velocityX, float velocityY, float velocityZ) => owner.Calls.Add(("Entity", "SendAddProjectile", [entityUniqueId, entityRuntimeId, x, y, z, velocityX, velocityY, velocityZ]));
        public void SendAddFallingBlock(long entityUniqueId, ulong entityRuntimeId, int blockRuntimeId, float x, float y, float z) => owner.Calls.Add(("Entity", "SendAddFallingBlock", [entityUniqueId, entityRuntimeId, blockRuntimeId, x, y, z]));
        public void SendRemoveActor(long actorUniqueId) => owner.Calls.Add(("Entity", "SendRemoveActor", [actorUniqueId]));
        public void SendMoveActorAbsoluteRaw(ulong actorRuntimeId, float x, float y, float z, byte flags = 0, float pitch = 0f, float yaw = 0f, float headYaw = 0f) => owner.Calls.Add(("Entity", "SendMoveActorAbsoluteRaw", [actorRuntimeId, x, y, z, flags, pitch, yaw, headYaw]));
        public void SendMoveActorAbsoluteRaws(IReadOnlyList<RawActorPose> poses) => owner.Calls.Add(("Entity", "SendMoveActorAbsoluteRaws", [poses]));
        public void SendMoveAbsolutes(IReadOnlyList<AbsoluteActorPose> poses) => owner.Calls.Add(("Entity", "SendMoveAbsolutes", [poses]));
        public void SendMovePlayerTeleport(ulong entityRuntimeId, float x, float y, float z, float pitch, float yaw, float headYaw, ulong tick = 0) => owner.Calls.Add(("Entity", "SendMovePlayerTeleport", [entityRuntimeId, x, y, z, pitch, yaw, headYaw, tick]));
        public void SendSetActorLink(long riderUniqueId, long riddenUniqueId, byte linkType, bool immediate = true, bool causedByRider = true) => owner.Calls.Add(("Entity", "SendSetActorLink", [riderUniqueId, riddenUniqueId, linkType, immediate, causedByRider]));
        public void SendActorFlags(ulong actorRuntimeId, bool sneaking, bool sprinting) => owner.Calls.Add(("Entity", "SendActorFlags", [actorRuntimeId, sneaking, sprinting]));
        public void SendActorIgnited(ulong actorRuntimeId, bool ignited) => owner.Calls.Add(("Entity", "SendActorIgnited", [actorRuntimeId, ignited]));
        public void SendHealth(ulong actorRuntimeId, float health, float maximum) => owner.Calls.Add(("Entity", "SendHealth", [actorRuntimeId, health, maximum]));
        public void SendHurt(ulong actorRuntimeId) => owner.Calls.Add(("Entity", "SendHurt", [actorRuntimeId]));
        public void SendDeath(ulong actorRuntimeId) => owner.Calls.Add(("Entity", "SendDeath", [actorRuntimeId]));
        public void SendAttackSwing(ulong actorRuntimeId) => owner.Calls.Add(("Entity", "SendAttackSwing", [actorRuntimeId]));
        public void SendKnockback(ulong actorRuntimeId, float velocityX, float velocityY, float velocityZ) => owner.Calls.Add(("Entity", "SendKnockback", [actorRuntimeId, velocityX, velocityY, velocityZ]));
        public void SendAnimateSwingArm(ulong actorRuntimeId, string? swingSource = "attack") => owner.Calls.Add(("Entity", "SendAnimateSwingArm", [actorRuntimeId, swingSource]));
        public void SendTakeItemActor(ulong itemEntityRuntimeId, ulong takerEntityRuntimeId) => owner.Calls.Add(("Entity", "SendTakeItemActor", [itemEntityRuntimeId, takerEntityRuntimeId]));
        public void SendMobEffect(ulong actorRuntimeId, byte eventId, int effectId, int amplifier, bool showParticles, int durationTicks) => owner.Calls.Add(("Entity", "SendMobEffect", [actorRuntimeId, eventId, effectId, amplifier, showParticles, durationTicks]));
        public void SendFloorDropActor(long entityRuntimeId, StackId stackId, int count, float x, float y, float z, float velocityX = 0f, float velocityY = 0f, float velocityZ = 0f) => owner.Calls.Add(("Entity", "SendFloorDropActor", [entityRuntimeId, stackId, count, x, y, z, velocityX, velocityY, velocityZ]));
        public void SendPlayerAttributes(Player.Player player) => owner.Calls.Add(("Entity", "SendPlayerAttributes", [player]));
        public void SendSelectedHotbarSlot(int slot) => owner.Calls.Add(("Entity", "SendSelectedHotbarSlot", [slot]));
        public void SendPlayerGameType(int wireGameMode) => owner.Calls.Add(("Entity", "SendPlayerGameType", [wireGameMode]));
        public void SendLocalAbilities(long uniqueId, int wireGameMode, bool flying = true) => owner.Calls.Add(("Entity", "SendLocalAbilities", [uniqueId, wireGameMode, flying]));
        public void SendAdventureSettings() => owner.Calls.Add(("Entity", "SendAdventureSettings", []));
        public void SendDeathInfo(string cause, params string[] messages) => owner.Calls.Add(("Entity", "SendDeathInfo", [cause, messages]));
        public void SendRespawnSearching(float eyeX, float eyeY, float eyeZ, ulong entityRuntimeId) => owner.Calls.Add(("Entity", "SendRespawnSearching", [eyeX, eyeY, eyeZ, entityRuntimeId]));
        public void SendRespawnReady(float eyeX, float eyeY, float eyeZ, ulong entityRuntimeId) => owner.Calls.Add(("Entity", "SendRespawnReady", [eyeX, eyeY, eyeZ, entityRuntimeId]));
        public void SendMobEquipment(ulong actorRuntimeId, PlayerInventory inventory, int hotbarSlot) => owner.Calls.Add(("Entity", "SendMobEquipment", [actorRuntimeId, inventory, hotbarSlot]));
        public void SendMobArmorEquipment(ulong actorRuntimeId, PlayerInventory inventory) => owner.Calls.Add(("Entity", "SendMobArmorEquipment", [actorRuntimeId, inventory]));
        public void SendMobOffhandEquipment(ulong actorRuntimeId, PlayerInventory inventory) => owner.Calls.Add(("Entity", "SendMobOffhandEquipment", [actorRuntimeId, inventory]));
        public void SendPlayerListEntryOf(Player.Player subject) => owner.Calls.Add(("Entity", "SendPlayerListEntryOf", [subject]));
        public void SendPlayerListRemove(Guid uuid) => owner.Calls.Add(("Entity", "SendPlayerListRemove", [uuid]));
        public void SendPlayerActorOf(Player.Player subject) => owner.Calls.Add(("Entity", "SendPlayerActorOf", [subject]));
    }

    private sealed class InventoryRecorder(RecordingClientChannel owner) : IInventoryChannel
    {
        public void SendInventoryContent(PlayerInventory inventory) => owner.Calls.Add(("Inventory", "SendInventoryContent", [inventory]));
        public void SendUiInventoryContent(Player.Player player) => owner.Calls.Add(("Inventory", "SendUiInventoryContent", [player]));
        public void SendArmorContent(PlayerInventory inventory) => owner.Calls.Add(("Inventory", "SendArmorContent", [inventory]));
        public void SendOffhandContent(PlayerInventory inventory) => owner.Calls.Add(("Inventory", "SendOffhandContent", [inventory]));
        public void SendCreativeContent() => owner.Calls.Add(("Inventory", "SendCreativeContent", []));
        public void SendChestContent(ChestStore chests, in OpenChestView view) => owner.Calls.Add(("Inventory", "SendChestContent", [chests, view]));
        public void SendContainerOpen(int blockX, int blockY, int blockZ) => owner.Calls.Add(("Inventory", "SendContainerOpen", [blockX, blockY, blockZ]));
        public void SendChestOpen(int blockX, int blockY, int blockZ) => owner.Calls.Add(("Inventory", "SendChestOpen", [blockX, blockY, blockZ]));
        public void SendCraftingTableOpen(int blockX, int blockY, int blockZ) => owner.Calls.Add(("Inventory", "SendCraftingTableOpen", [blockX, blockY, blockZ]));
        public void SendContainerClose(byte windowId, byte windowType) => owner.Calls.Add(("Inventory", "SendContainerClose", [windowId, windowType]));
        public void BeginOpenContainerSession(uint generation) => owner.Calls.Add(("Inventory", "BeginOpenContainerSession", [generation]));
        public void EndOpenContainerSession() => owner.Calls.Add(("Inventory", "EndOpenContainerSession", []));
        public void SendItemStackResponseOk(int requestId, Player.Player player, IReadOnlyList<WireTouch> wireTouches) => owner.Calls.Add(("Inventory", "SendItemStackResponseOk", [requestId, player, wireTouches]));
        public void SendItemStackResponseError(int requestId) => owner.Calls.Add(("Inventory", "SendItemStackResponseError", [requestId]));
        public bool MatchesAdvertisedStackNetId(in InventorySlotReference reference, int clientStackNetId) { owner.Calls.Add(("Inventory", "MatchesAdvertisedStackNetId", [reference, clientStackNetId])); return true; }
    }

    private sealed class WorldRecorder(RecordingClientChannel owner) : IWorldChannel
    {
        public void SendChunkPublisher(int blockX, int blockY, int blockZ, int radiusBlocks) => owner.Calls.Add(("World", "SendChunkPublisher", [blockX, blockY, blockZ, radiusBlocks]));
        public void SendWorldSpawnPosition(int x, int y, int z) => owner.Calls.Add(("World", "SendWorldSpawnPosition", [x, y, z]));
        public void SendSpawnComplete() => owner.Calls.Add(("World", "SendSpawnComplete", []));
        public void SendTime(int worldTime) => owner.Calls.Add(("World", "SendTime", [worldTime]));
        public void SendGameRules(IReadOnlyList<GameRule> rules) => owner.Calls.Add(("World", "SendGameRules", [rules]));
        public void SendUpdateBlock(int x, int y, int z, int blockRuntimeId) => owner.Calls.Add(("World", "SendUpdateBlock", [x, y, z, blockRuntimeId]));
        public void PublishUpdateBlocks(IReadOnlyList<(int X, int Y, int Z, int BlockRuntimeId)> updates) => owner.Calls.Add(("World", "PublishUpdateBlocks", [updates]));
        public void SendLevelSoundEvent(string sound, float x, float y, float z, int extraData = -1, bool isGlobal = false) => owner.Calls.Add(("World", "SendLevelSoundEvent", [sound, x, y, z, extraData, isGlobal]));
        public void SendLevelEvent(int eventType, float x, float y, float z, int eventData = 0) => owner.Calls.Add(("World", "SendLevelEvent", [eventType, x, y, z, eventData]));
        public void SendChestLidOpen(int blockX, int blockY, int blockZ) => owner.Calls.Add(("World", "SendChestLidOpen", [blockX, blockY, blockZ]));
        public void SendChestLidClose(int blockX, int blockY, int blockZ) => owner.Calls.Add(("World", "SendChestLidClose", [blockX, blockY, blockZ]));
        public void SendBlockStartCrack(int blockX, int blockY, int blockZ, int breakTicks) => owner.Calls.Add(("World", "SendBlockStartCrack", [blockX, blockY, blockZ, breakTicks]));
        public void SendBlockStopCrack(int blockX, int blockY, int blockZ) => owner.Calls.Add(("World", "SendBlockStopCrack", [blockX, blockY, blockZ]));
        public void SendBlockBreakSpeed(int blockX, int blockY, int blockZ, int breakTicks) => owner.Calls.Add(("World", "SendBlockBreakSpeed", [blockX, blockY, blockZ, breakTicks]));
    }

    private sealed class ChatRecorder(RecordingClientChannel owner) : IChatChannel
    {
        public void SendSystem(string message) => owner.Calls.Add(("Chat", "SendSystem", [message]));
        public void SendChat(string sourceName, string message, string xboxUserId = "") => owner.Calls.Add(("Chat", "SendChat", [sourceName, message, xboxUserId]));
    }

    private sealed class UiRecorder(RecordingClientChannel owner) : IUiChannel
    {
        public void SendToast(string title, string content) => owner.Calls.Add(("Ui", "SendToast", [title, content]));
    }

}
