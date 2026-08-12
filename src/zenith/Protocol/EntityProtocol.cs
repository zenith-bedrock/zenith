using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Pose for batched MoveActorAbsolute (Gameplay fills; Protocol serializes).</summary>
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

/// <summary>Raw non-player actor pose; the domain Y is already the actor base position.</summary>
readonly struct RawActorPose
{
    public ulong ActorRuntimeId { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public byte Flags { get; init; }
}

/// <summary>Transmite intenções de entidade. Sem lógica de gameplay.</summary>
sealed class EntityProtocol
{
    private readonly NetworkSession _session;

    public EntityProtocol(NetworkSession session) => _session = session;

    /// <param name="y">Domain feet Y — wire adds <see cref="EntityHitboxes.PlayerNetworkOffset"/>.</param>
    public void SendMoveAbsolute(
        ulong actorRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        byte flags = 0)
    {
        _session.SendDataPacket(CreateMoveAbsolute(actorRuntimeId, x, y, z, pitch, yaw, headYaw, flags));
    }

    /// <summary>One GamePacket envelope for N Absolute moves (ADR §44).</summary>
    public void SendMoveAbsolutes(IReadOnlyList<AbsoluteActorPose> poses)
    {
        if (poses.Count == 0) return;
        if (poses.Count == 1)
        {
            var p = poses[0];
            SendMoveAbsolute(
                p.ActorRuntimeId, p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.HeadYaw,
                p.OnGround ? MoveActorAbsolutePacket.FLAG_ON_GROUND : (byte)0);
            return;
        }

        var packets = new DataPacket[poses.Count];
        for (var i = 0; i < poses.Count; i++)
        {
            var p = poses[i];
            packets[i] = CreateMoveAbsolute(
                p.ActorRuntimeId, p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.HeadYaw,
                p.OnGround ? MoveActorAbsolutePacket.FLAG_ON_GROUND : (byte)0);
        }

        _session.SendDataPacket(packets);
    }

    /// <summary>
    /// <paramref name="y"/> is domain feet; packet Y is <see cref="EntityHitboxes.AbsoluteWireY"/>.
    /// </summary>
    private static MoveActorAbsolutePacket CreateMoveAbsolute(
        ulong actorRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        byte flags = 0) =>
        new()
        {
            ActorRuntimeId = actorRuntimeId,
            Flags = flags,
            PositionX = x,
            PositionY = EntityHitboxes.AbsoluteWireY(y),
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw
        };

    /// <summary>
    /// Non-player actor move — no <see cref="EntityHitboxes.AbsoluteWireY"/> eye offset (that's a
    /// player-only wire convention). <paramref name="y"/> is the actor's own base position (ADR §95).
    /// </summary>
    public void SendMoveActorAbsoluteRaw(ulong actorRuntimeId, float x, float y, float z, byte flags = 0)
    {
        _session.SendDataPacket(new MoveActorAbsolutePacket
        {
            ActorRuntimeId = actorRuntimeId,
            Flags = flags,
            PositionX = x,
            PositionY = y,
            PositionZ = z
        });
    }

    /// <summary>
    /// Local camera snap — MovePlayer Teleport (ADR §41). Not for peers.
    /// <paramref name="y"/> is domain feet; packet Y is <see cref="EntityHitboxes.AbsoluteWireY"/>.
    /// </summary>
    public void SendMovePlayerTeleport(
        ulong entityRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        ulong tick = 0)
    {
        _session.SendDataPacket(MovePlayerPacket.CreateTeleport(
            entityRuntimeId, x, EntityHitboxes.AbsoluteWireY(y), z, pitch, yaw, headYaw, tick));
    }

    public void SendPlayerListAdd(
        Guid uuid,
        long actorUniqueId,
        string username,
        SerializedSkin? skin = null,
        byte[]? skinRgba = null,
        uint skinWidth = 0,
        uint skinHeight = 0,
        bool verified = false,
        string xboxUserId = "",
        string platformChatId = "",
        int buildPlatform = -1)
    {
        _session.SendDataPacket(new PlayerListPacket
        {
            Type = PlayerListPacket.TypeAdd,
            Entries =
            [
                PlayerListEntry.ForAdd(
                    uuid,
                    actorUniqueId,
                    username,
                    skin,
                    skinRgba,
                    skinWidth,
                    skinHeight,
                    verified,
                    xboxUserId,
                    platformChatId,
                    buildPlatform)
            ]
        });
    }

    public void SendPlayerListRemove(Guid uuid)
    {
        _session.SendDataPacket(new PlayerListPacket
        {
            Type = PlayerListPacket.TypeRemove,
            Entries = [PlayerListEntry.ForRemove(uuid)]
        });
    }

    public void SendAddPlayer(
        Guid uuid,
        string username,
        ulong actorRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        NetworkItemStack heldItem,
        int gameMode = AbilityBits.WireGameModeSurvival,
        bool sneaking = false,
        bool sprinting = false,
        string platformChatId = "",
        string deviceId = "",
        int buildPlatform = -1)
    {
        _session.SendDataPacket(new AddPlayerPacket
        {
            Uuid = uuid,
            Username = username,
            ActorRuntimeId = actorRuntimeId,
            PlatformChatId = platformChatId,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw,
            HeldItem = heldItem,
            GameMode = gameMode,
            Sneaking = sneaking,
            Sprinting = sprinting,
            DeviceId = deviceId,
            BuildPlatform = buildPlatform
        });
    }

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

    public void SendRemoveActor(long actorUniqueId)
    {
        _session.SendDataPacket(new RemoveActorPacket { ActorUniqueId = actorUniqueId });
    }

    /// <summary>Falling-block actor (ADR §95). <paramref name="y"/> is block-anchored, no eye offset.</summary>
    public void SendAddFallingBlock(long entityUniqueId, ulong entityRuntimeId, int blockRuntimeId, float x, float y, float z)
    {
        _session.SendDataPacket(new AddActorPacket
        {
            EntityUniqueId = entityUniqueId,
            EntityRuntimeId = entityRuntimeId,
            EntityType = "minecraft:falling_block",
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Variant = blockRuntimeId
        });
    }

    /// <summary>Dropped item entity at cell center (ADR §26 wire). Velocity always zero in MVP.</summary>
    public void SendAddItemActor(
        long entityRuntimeId,
        NetworkItemStack item,
        float x,
        float y,
        float z)
    {
        _session.SendDataPacket(new AddItemActorPacket
        {
            EntityUniqueId = entityRuntimeId,
            EntityRuntimeId = (ulong)entityRuntimeId,
            Item = item,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            FromFishing = false
        });
    }

    public void SendAddZombie(long entityUniqueId, ulong entityRuntimeId, float x, float y, float z, float yaw)
    {
        _session.SendDataPacket(new AddActorPacket
        {
            EntityUniqueId = entityUniqueId,
            EntityRuntimeId = entityRuntimeId,
            EntityType = "minecraft:zombie",
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Yaw = yaw,
            HeadYaw = yaw,
            BodyYaw = yaw,
            ZombieMetadata = true
        });
    }

    public void SendAddSkeleton(long entityUniqueId, ulong entityRuntimeId, float x, float y, float z, float yaw)
    {
        _session.SendDataPacket(new AddActorPacket
        {
            EntityUniqueId = entityUniqueId, EntityRuntimeId = entityRuntimeId,
            EntityType = "minecraft:skeleton", PositionX = x, PositionY = y, PositionZ = z,
            Yaw = yaw, HeadYaw = yaw, BodyYaw = yaw
        });
    }

    /// <summary>One transport submission for several raw non-player actor movements.</summary>
    public void SendMoveActorAbsoluteRaws(IReadOnlyList<RawActorPose> poses)
    {
        if (poses.Count == 0) return;
        if (poses.Count == 1)
        {
            var p = poses[0];
            SendMoveActorAbsoluteRaw(p.ActorRuntimeId, p.X, p.Y, p.Z, p.Flags);
            return;
        }

        var packets = new MoveActorAbsolutePacket[poses.Count];
        for (var i = 0; i < poses.Count; i++)
        {
            var p = poses[i];
            packets[i] = new MoveActorAbsolutePacket
            {
                ActorRuntimeId = p.ActorRuntimeId,
                Flags = p.Flags,
                PositionX = p.X,
                PositionY = p.Y,
                PositionZ = p.Z
            };
        }
        _session.SendDataPacket(packets);
    }

    /// <summary>Short-lived snowball projection for the concrete ProjectileSystem slice.</summary>
    public void SendAddProjectile(
        long entityUniqueId, ulong entityRuntimeId,
        float x, float y, float z, float velocityX, float velocityY, float velocityZ)
    {
        _session.SendDataPacket(new AddActorPacket
        {
            EntityUniqueId = entityUniqueId,
            EntityRuntimeId = entityRuntimeId,
            EntityType = "minecraft:snowball",
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            VelocityX = velocityX,
            VelocityY = velocityY,
            VelocityZ = velocityZ
        });
    }

    /// <summary>
    /// Adapts an authoritative floor-drop stack to the Bedrock item representation before
    /// transmitting its actor. Gameplay owns whether a drop exists; wire conversion stays here.
    /// </summary>
    public void SendFloorDropActor(long entityRuntimeId, StackId stackId, int count, float x, float y, float z)
    {
        var item = _session.Protocol.Inventory.DescribeStack(stackId, count);
        if (item.NetworkId == 0) return; // invalid/air item crashes Bedrock near player
        SendAddItemActor(entityRuntimeId, item, x, y, z);
    }

    public void SendTakeItemActor(ulong itemEntityRuntimeId, ulong takerEntityRuntimeId)
    {
        _session.SendDataPacket(new TakeItemActorPacket
        {
            ItemEntityRuntimeId = itemEntityRuntimeId,
            TakerEntityRuntimeId = takerEntityRuntimeId
        });
    }

    /// <summary>Local-player metadata seed (Breathing + pose) — HUD honesty at spawn (§34 / §53).</summary>
    public void SendLocalActorData(
        ulong actorRuntimeId,
        string name,
        bool sneaking = false,
        bool sprinting = false)
    {
        _session.SendDataPacket(new SetActorDataPacket
        {
            ActorRuntimeId = actorRuntimeId,
            Name = name,
            Tick = 0,
            Sneaking = sneaking,
            Sprinting = sprinting
        });
    }

    /// <summary>Peer pose FLAGS-only update (sneak/sprint) — §53.</summary>
    public void SendActorFlags(ulong actorRuntimeId, bool sneaking, bool sprinting)
    {
        _session.SendDataPacket(new SetActorDataPacket
        {
            ActorRuntimeId = actorRuntimeId,
            FlagsOnly = true,
            Sneaking = sneaking,
            Sprinting = sprinting,
            Tick = 0
        });
    }

    /// <summary>Arm swing to a viewer (§53).</summary>
    /// <summary>Arm swing to a viewer (§53). <paramref name="swingSource"/> e.g. attack/mine/build.</summary>
    public void SendAnimateSwingArm(ulong actorRuntimeId, string? swingSource = "attack")
    {
        _session.SendDataPacket(new AnimatePacket
        {
            Action = AnimatePacket.ActionSwingArm,
            ActorRuntimeId = actorRuntimeId,
            Data = 0f,
            SwingSource = swingSource
        });
    }

    /// <summary>Emote rebroadcast to a viewer (§53).</summary>
    public void SendEmote(
        ulong actorRuntimeId,
        string emoteId,
        uint tickLength,
        string xuid,
        string platformChatId,
        byte flags)
    {
        _session.SendDataPacket(new EmotePacket
        {
            ActorRuntimeId = actorRuntimeId,
            EmoteId = emoteId,
            TickLength = tickLength,
            Xuid = xuid,
            PlatformChatId = platformChatId,
            Flags = flags
        });
    }

    /// <summary>Attributes from Player vitals (ADR §40) — remaining fields still seed constants.</summary>
    public void SendDefaultAttributes(ulong actorRuntimeId, float health, float hunger)
    {
        _session.SendDataPacket(UpdateAttributesPacket.CreateDefaults(actorRuntimeId, health, hunger));
    }

    /// <summary>Replicates only the current health of an already-spawned actor.</summary>
    public void SendHealth(ulong actorRuntimeId, float health, float maximum)
    {
        _session.SendDataPacket(UpdateAttributesPacket.CreateHealth(actorRuntimeId, health, maximum));
    }

    /// <summary>Death screen cause (DeathInfo 0xbd).</summary>
    public void SendDeathInfo(string cause, params string[] messages)
    {
        _session.SendDataPacket(new DeathInfoPacket
        {
            Cause = cause,
            Messages = messages
        });
    }

    /// <summary>Respawn handshake state (0x2d). Position is eye-space like StartGame.</summary>
    public void SendRespawn(float eyeX, float eyeY, float eyeZ, byte state, ulong entityRuntimeId)
    {
        _session.SendDataPacket(new RespawnPacket
        {
            PositionX = eyeX,
            PositionY = eyeY,
            PositionZ = eyeZ,
            State = state,
            EntityRuntimeId = entityRuntimeId
        });
    }

    /// <summary>Server → client: searching for spawn (Gameplay must not reference Packets constants).</summary>
    public void SendRespawnSearching(float eyeX, float eyeY, float eyeZ, ulong entityRuntimeId) =>
        SendRespawn(eyeX, eyeY, eyeZ, RespawnPacket.StateSearchingForSpawn, entityRuntimeId);

    /// <summary>Server → client: ready to spawn at position.</summary>
    public void SendRespawnReady(float eyeX, float eyeY, float eyeZ, ulong entityRuntimeId) =>
        SendRespawn(eyeX, eyeY, eyeZ, RespawnPacket.StateReadyToSpawn, entityRuntimeId);

    /// <summary>Local UpdateAbilities seed / RequestAbility echo — runtime id = unique id (§37).</summary>
    public void SendLocalAbilities(long uniqueId, int wireGameMode, bool flying = true)
    {
        _session.SendDataPacket(UpdateAbilitiesPacket.Create(uniqueId, wireGameMode, flying));
    }

    /// <summary>LAN UpdateAdventureSettings defaults (§37).</summary>
    public void SendAdventureSettings()
    {
        _session.SendDataPacket(UpdateAdventureSettingsPacket.CreateLanDefaults());
    }

    /// <summary>Runtime SetPlayerGameType after GameLoop applies mode (§52).</summary>
    public void SendPlayerGameType(int wireGameMode) =>
        _session.SendDataPacket(new SetPlayerGameTypePacket { GameType = wireGameMode });
}
