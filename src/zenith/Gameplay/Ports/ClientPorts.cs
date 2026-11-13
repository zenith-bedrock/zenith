using Zenith.Player;
using Zenith.Rulebook;
using Zenith.World;

namespace Zenith.Gameplay.Ports;

/// <summary>
/// What gameplay may ask of one connected client. Gameplay owns this contract; the Bedrock protocol layer implements it
/// (dependency inversion), so no gameplay type names a packet, a session or a protocol class, and a test can stand in a
/// recording fake. The members are what gameplay needs to say ("this actor appeared", "this container opened"), not how the
/// wire says it.
/// </summary>
interface IClientChannel
{
    IEntityChannel Entity { get; }
    IInventoryChannel Inventory { get; }
    IWorldChannel World { get; }
    IChatChannel Chat { get; }
    IUiChannel Ui { get; }

    /// <summary>True once the connection is gone; sends to a closed client are dropped.</summary>
    bool IsClosed { get; }

    /// <summary>The client's Xbox user id (empty for an offline login).</summary>
    string Xuid { get; }
}

/// <summary>The kinds of mob a client can be told about; each maps to one Bedrock actor type in the protocol layer.</summary>
enum MobKind { Zombie, Villager, Spider, Skeleton, Minecart, Golem, Fish, Enderman, Creeper, Cow, Bat }

/// <summary>Pose for a batched absolute move (gameplay fills it, the protocol layer serializes it).</summary>
readonly struct AbsoluteActorPose
{
    public ulong ActorRuntimeId { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float Pitch { get; init; }
    public float Yaw { get; init; }
    public float HeadYaw { get; init; }
    public bool OnGround { get; init; }
}

/// <summary>
/// Raw non-player actor pose; the domain Y is already the actor base position. Yaw/HeadYaw default to 0 for callers that
/// genuinely have no orientation (falling blocks, projectiles). Every mob system must pass its actual current yaw, or every
/// post-spawn movement update silently resets the actor to face +Z.
/// </summary>
readonly struct RawActorPose
{
    public ulong ActorRuntimeId { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float Pitch { get; init; }
    public float Yaw { get; init; }
    public float HeadYaw { get; init; }
    public byte Flags { get; init; }
}

interface IEntityChannel
{
    void SendAddMob(MobKind kind, long entityUniqueId, ulong entityRuntimeId, float x, float y, float z, float yaw);
    void SendAddProjectile(long entityUniqueId, ulong entityRuntimeId, float x, float y, float z, float velocityX, float velocityY, float velocityZ);
    void SendAddFallingBlock(long entityUniqueId, ulong entityRuntimeId, int blockRuntimeId, float x, float y, float z);
    void SendRemoveActor(long actorUniqueId);

    void SendMoveActorAbsoluteRaw(ulong actorRuntimeId, float x, float y, float z, byte flags = 0, float pitch = 0f, float yaw = 0f, float headYaw = 0f);
    void SendMoveActorAbsoluteRaws(IReadOnlyList<RawActorPose> poses);
    void SendMoveAbsolutes(IReadOnlyList<AbsoluteActorPose> poses);
    void SendMovePlayerTeleport(ulong entityRuntimeId, float x, float y, float z, float pitch, float yaw, float headYaw, ulong tick = 0);

    void SendSetActorLink(long riderUniqueId, long riddenUniqueId, byte linkType, bool immediate = true, bool causedByRider = true);
    void SendActorFlags(ulong actorRuntimeId, bool sneaking, bool sprinting);
    void SendActorIgnited(ulong actorRuntimeId, bool ignited);

    void SendHealth(ulong actorRuntimeId, float health, float maximum);
    void SendHurt(ulong actorRuntimeId);
    void SendDeath(ulong actorRuntimeId);
    void SendAttackSwing(ulong actorRuntimeId);
    void SendKnockback(ulong actorRuntimeId, float velocityX, float velocityY, float velocityZ);
    void SendAnimateSwingArm(ulong actorRuntimeId, string? swingSource = "attack");
    void SendTakeItemActor(ulong itemEntityRuntimeId, ulong takerEntityRuntimeId);
    void SendMobEffect(ulong actorRuntimeId, byte eventId, int effectId, int amplifier, bool showParticles, int durationTicks);

    void SendFloorDropActor(long entityRuntimeId, StackId stackId, int count, float x, float y, float z, float velocityX = 0f, float velocityY = 0f, float velocityZ = 0f);

    void SendPlayerAttributes(Player.Player player);
    void SendSelectedHotbarSlot(int slot);
    void SendPlayerGameType(int wireGameMode);
    void SendLocalAbilities(long uniqueId, int wireGameMode, bool flying = true);
    void SendAdventureSettings();

    void SendDeathInfo(string cause, params string[] messages);
    void SendRespawnSearching(float eyeX, float eyeY, float eyeZ, ulong entityRuntimeId);
    void SendRespawnReady(float eyeX, float eyeY, float eyeZ, ulong entityRuntimeId);

    /// <summary>Equipment of <paramref name="actorRuntimeId"/>: the held stack of <paramref name="inventory"/> at <paramref name="hotbarSlot"/>.</summary>
    void SendMobEquipment(ulong actorRuntimeId, PlayerInventory inventory, int hotbarSlot);
    void SendMobArmorEquipment(ulong actorRuntimeId, PlayerInventory inventory);
    void SendMobOffhandEquipment(ulong actorRuntimeId, PlayerInventory inventory);

    /// <summary>Tells this client that <paramref name="subject"/> exists in the player list (identity, skin, platform).</summary>
    void SendPlayerListEntryOf(Player.Player subject);
    void SendPlayerListRemove(Guid uuid);
    /// <summary>Spawns <paramref name="subject"/> as a peer actor for this client, with its held item and equipment.</summary>
    void SendPlayerActorOf(Player.Player subject);
}

interface IInventoryChannel
{
    void SendInventoryContent(PlayerInventory inventory);
    void SendUiInventoryContent(Player.Player player);
    void SendArmorContent(PlayerInventory inventory);
    void SendOffhandContent(PlayerInventory inventory);
    void SendCreativeContent();
    void SendChestContent(ChestStore chests, in OpenChestView view);

    void SendContainerOpen(int blockX, int blockY, int blockZ);
    void SendChestOpen(int blockX, int blockY, int blockZ);
    void SendCraftingTableOpen(int blockX, int blockY, int blockZ);
    void SendContainerClose(byte windowId, byte windowType);
    void BeginOpenContainerSession(uint generation);
    void EndOpenContainerSession();

    void SendItemStackResponseOk(int requestId, Player.Player player, IReadOnlyList<WireTouch> wireTouches);
    void SendItemStackResponseError(int requestId);
    /// <summary>True when the stack network id the client sent is the one this server last advertised for the slot.</summary>
    bool MatchesAdvertisedStackNetId(in InventorySlotReference reference, int clientStackNetId);
}

interface IWorldChannel
{
    void SendChunkPublisher(int blockX, int blockY, int blockZ, int radiusBlocks);
    void SendWorldSpawnPosition(int x, int y, int z);
    void SendSpawnComplete();
    void SendTime(int worldTime);
    void SendGameRules(IReadOnlyList<GameRule> rules);

    void SendUpdateBlock(int x, int y, int z, int blockRuntimeId);
    void PublishUpdateBlocks(IReadOnlyList<(int X, int Y, int Z, int BlockRuntimeId)> updates);

    void SendLevelSoundEvent(string sound, float x, float y, float z, int extraData = -1, bool isGlobal = false);
    void SendLevelEvent(int eventType, float x, float y, float z, int eventData = 0);
    void SendChestLidOpen(int blockX, int blockY, int blockZ);
    void SendChestLidClose(int blockX, int blockY, int blockZ);
    void SendBlockStartCrack(int blockX, int blockY, int blockZ, int breakTicks);
    void SendBlockStopCrack(int blockX, int blockY, int blockZ);
    void SendBlockBreakSpeed(int blockX, int blockY, int blockZ, int breakTicks);
}

interface IChatChannel
{
    void SendSystem(string message);
    void SendChat(string sourceName, string message, string xboxUserId = "");
}

interface IUiChannel
{
    void SendToast(string title, string content);
}
