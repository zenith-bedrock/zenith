using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>What happens to an actor that peers should see: effects, hurt, swings, death, knockback, emotes.</summary>
sealed partial class EntityProtocol
{
    /// <summary>Timed player effect add/update/remove (Phase XI.3) — <see cref="MobEffectPacket"/> events.</summary>
    public void SendMobEffect(
        ulong actorRuntimeId, byte eventId, int effectId, int amplifier, bool showParticles, int durationTicks)
    {
        _session.SendDataPacket(new MobEffectPacket
        {
            TargetRuntimeId = actorRuntimeId,
            EventId = eventId,
            EffectId = effectId,
            EffectAmplifier = amplifier,
            ShowParticles = showParticles,
            EffectDurationTicks = durationTicks,
            Tick = ProcessedInputTick(actorRuntimeId),
            Ambient = false
        });
    }

    /// <summary>
    /// Non-lethal damage reaction (Phase XXIII) — ActorEvent HURT_ANIMATION. Send once per landed
    /// hit that did not kill the target; gameplay decides "hurt happened", this only projects it.
    /// </summary>
    public void SendHurt(ulong actorRuntimeId)
    {
        _session.SendDataPacket(new ActorEventPacket
        {
            ActorRuntimeId = actorRuntimeId,
            EventId = ActorEventPacket.EventHurt,
            EventData = 0
        });
    }

    /// <summary>
    /// Mob attack-swing animation (Phase XXIII-B) — ActorEvent ARM_SWING, not the player-only
    /// AnimatePacket. See <see cref="ActorEventPacket.EventArmSwing"/>.
    /// </summary>
    public void SendAttackSwing(ulong actorRuntimeId)
    {
        _session.SendDataPacket(new ActorEventPacket
        {
            ActorRuntimeId = actorRuntimeId,
            EventId = ActorEventPacket.EventArmSwing,
            EventData = 0
        });
    }

    /// <summary>
    /// Death reaction (Phase XXIII) — ActorEvent DEATH_ANIMATION. Sent immediately before
    /// <see cref="SendRemoveActor"/>; PocketMine's reference behavior fires this as a one-shot event
    /// tolerating immediate removal, so no separate "dying" ECS lifecycle state was introduced (see
    /// docs/history/phases/phase-xxiii-entity-fidelity-findings.md).
    /// </summary>
    public void SendDeath(ulong actorRuntimeId)
    {
        _session.SendDataPacket(new ActorEventPacket
        {
            ActorRuntimeId = actorRuntimeId,
            EventId = ActorEventPacket.EventDeath,
            EventData = 0
        });
    }

    /// <summary>
    /// Player-facing knockback impulse (Phase XXIII-B) — SetActorMotion. The player's own client
    /// physics integrates this velocity (gravity/friction) same as a mob's SetActorMotion; the
    /// server does not (and cannot) move a player's position directly.
    /// </summary>
    public void SendKnockback(ulong actorRuntimeId, float velocityX, float velocityY, float velocityZ)
    {
        _session.SendDataPacket(new SetActorMotionPacket
        {
            ActorRuntimeId = actorRuntimeId,
            VelocityX = velocityX,
            VelocityY = velocityY,
            VelocityZ = velocityZ,
            Tick = ProcessedInputTick(actorRuntimeId)
        });
    }

    private ulong ProcessedInputTick(ulong actorRuntimeId) => actorRuntimeId <= long.MaxValue
        ? _session.Context.PlayerManager.GetByRuntimeId((long)actorRuntimeId)?.LastProcessedInputTick ?? 0
        : 0;

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
}
