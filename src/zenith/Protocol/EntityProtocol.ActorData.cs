using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Actor metadata and attributes: pose flags, air supply, health, ignition, the selected hotbar slot.</summary>
sealed partial class EntityProtocol
{
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
    public void SendAirSupply(ulong actorRuntimeId, short air, short max) =>
        _session.SendDataPacket(new SetActorDataPacket { ActorRuntimeId = actorRuntimeId, AirSupply = air, AirSupplyMax = max, Tick = 0 });

    public void SendActorFlags(ulong actorRuntimeId, bool sneaking, bool sprinting, bool usingItem = false, bool swimming = false)
    {
        _session.SendDataPacket(new SetActorDataPacket
        {
            ActorRuntimeId = actorRuntimeId,
            FlagsOnly = true,
            UsingItem = usingItem,
            Swimming = swimming,
            Sneaking = sneaking,
            Sprinting = sprinting,
            Tick = 0
        });
    }

    /// <summary>
    /// Creeper fuse-lit visual state (Phase XXIII fix — was never sent at all). Gameplay only
    /// decides "ignited happened"; the FLAGS bitmask itself is a wire concern owned here, not in
    /// CreeperSystem (see docs/history/phases/phase-xxiii-entity-fidelity-findings.md).
    /// </summary>
    public void SendActorIgnited(ulong actorRuntimeId, bool ignited)
    {
        var flags = EntityMetadataWriter.BuildMobFlags();
        if (ignited) flags |= EntityFlag.Bit(EntityFlag.Ignited);
        _session.SendDataPacket(new SetActorDataPacket
        {
            ActorRuntimeId = actorRuntimeId,
            FlagsOnly = true,
            RawFlags = flags,
            Tick = 0
        });
    }

    /// <summary>Attributes from Player vitals (ADR §40) plus level/experience (Phase XI.4).</summary>
    public void SendDefaultAttributes(
        ulong actorRuntimeId, float health, float hunger, int experienceLevel = 0, float experienceProgress = 0f,
        float saturation = 5f, float exhaustion = 0f)
    {
        _session.SendDataPacket(UpdateAttributesPacket.CreateDefaults(
            actorRuntimeId, health, hunger, experienceLevel, experienceProgress, saturation, exhaustion));
    }

    /// <summary>
    /// Projects a live <see cref="global::Zenith.Player.Player"/>'s current UpdateAttributes payload
    /// (Health/Hunger/Experience) in one call (Phase XII) — every one of six call sites had been
    /// hand-computing the same <c>(level, progress)</c> pair via <see cref="PlayerExperience.Progress"/>
    /// before this existed. Still a plain wire projection, not a state model: it reads
    /// <paramref name="player"/> and sends, nothing more.
    /// </summary>
    public void SendPlayerAttributes(global::Zenith.Player.Player player) =>
        SendDefaultAttributes(
            (ulong)player.RuntimeId, player.Health, player.Hunger,
            player.ExperienceLevel, PlayerExperience.Progress(player.ExperienceLevel, player.ExperiencePoints),
            player.Saturation, Math.Clamp(player.Exhaustion, 0f, 5f));

    /// <summary>Replicates only the current health of an already-spawned actor.</summary>
    public void SendHealth(ulong actorRuntimeId, float health, float maximum)
    {
        _session.SendDataPacket(UpdateAttributesPacket.CreateHealth(actorRuntimeId, health, maximum));
    }

    /// <summary>Legacy SetHealth for the local player (BDS sends it once at join, besides the health attribute).</summary>
    public void SendLocalHealth(float health) =>
        _session.SendDataPacket(new SetHealthPacket { Health = (int)MathF.Ceiling(health) });

    /// <summary>Tells the client which hotbar slot is selected (window 0, slot selected).</summary>
    public void SendSelectedHotbarSlot(int slot) =>
        _session.SendDataPacket(new PlayerHotbarPacket { SelectedSlot = slot, WindowId = 0, SelectSlot = true });
}
