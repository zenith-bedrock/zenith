using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Runtime;
using Zenith.Geometry;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// Advances experience orbs (plan 003, phase A): fall and slide like a dropped item, home in on the nearest player within
/// <see cref="TargetRange"/> blocks, are collected on touch and expire after <see cref="DespawnTicks"/>. An orb gives its points to
/// exactly one player, once: the credit and the removal from the store are the same step.
/// </summary>
[GameSystem("experience-orb")]
sealed class ExperienceOrbSystem : IGameSystem
{
    /// <summary>Ticks an orb lives (5 minutes, vanilla). It expires without giving anything.</summary>
    public const int DespawnTicks = 6000;

    /// <summary>How far a player attracts orbs, in blocks.</summary>
    public const float TargetRange = 8f;

    /// <summary>Ticks between looks for a target (vanilla: about once a second).</summary>
    public const int LookInterval = 20;

    /// <summary>Half the player's eye height: orbs aim at the middle of the body.</summary>
    private const float HalfEye = Blocks.PlayerEyeHeight / 2f;

    private readonly World.World _world;
    private readonly List<ExperienceOrbEntry> _scratch = [];

    public ExperienceOrbSystem(World.World world) => _world = world;

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var orbs = _world.ExperienceOrbs;
        if (orbs.Count == 0) return;
        if (clock.CurrentTick % MergeEvery == 0) MergeSettled(online);

        _scratch.Clear();
        _scratch.AddRange(orbs.Active);
        foreach (var orb in _scratch)
        {
            if (++orb.AgeTicks >= DespawnTicks)
            {
                Remove(orb, online);
                continue;
            }

            var target = FindTarget(orb, online);
            var pose = orb.Pose;

            // An orb in a column nobody keeps loaded waits where it is (a tick never wakes a chunk); its age still runs.
            if (_world.TryGetLoadedColumn(PlayerChunkTracker.BlockToChunk(pose.X), PlayerChunkTracker.BlockToChunk(pose.Z), out _))
            {
                if (target is not null) pose = Pull(pose, target);
                pose = FloorDropPhysics.Step(_world, pose, buoyant: false);
            }

            if (pose != orb.Pose)
            {
                orb.Pose = pose;
                foreach (var peer in online)
                    if (peer.IsInGame)
                        peer.Client.Entity.SendMoveActorAbsoluteRaw((ulong)orb.EntityId, pose.X, pose.Y, pose.Z, pose.OnGround ? (byte)1 : (byte)0);
            }

            if (target is not null && Touches(orb, target))
                Collect(orb, target, online);
        }
    }

    /// <summary>How often settled orbs are folded together.</summary>
    public const int MergeEvery = 10;

    /// <summary>Orbs resting this close (blocks) become one orb carrying both values.</summary>
    public const float MergeReach = 0.5f;

    /// <summary>
    /// Hundreds of kills must not mean hundreds of entities: two orbs that have come to rest near each other become one holding
    /// both values. The older orb stays (its look is unchanged on the clients) and the other is removed.
    /// </summary>
    private void MergeSettled(IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _scratch.Clear();
        _scratch.AddRange(_world.ExperienceOrbs.Active);
        for (var i = 0; i < _scratch.Count; i++)
        {
            var keep = _scratch[i];
            if (!IsResting(keep)) continue;
            for (var j = i + 1; j < _scratch.Count; j++)
            {
                var other = _scratch[j];
                if (!IsResting(other) || other.Value == 0) continue;
                if (MathF.Abs(keep.Pose.X - other.Pose.X) > MergeReach || MathF.Abs(keep.Pose.Y - other.Pose.Y) > MergeReach ||
                    MathF.Abs(keep.Pose.Z - other.Pose.Z) > MergeReach)
                    continue;

                keep.Value += other.Value;
                other.Value = 0; // spent: this sweep must not merge it again
                Remove(other, online);
            }
        }
    }

    private static bool IsResting(ExperienceOrbEntry orb) =>
        orb.Value > 0 && orb.Pose is { VelocityX: 0, VelocityY: 0, VelocityZ: 0 };

    /// <summary>The player this orb follows: the current one while still valid, else (looking once a second) the nearest valid one.</summary>
    private static global::Zenith.Player.Player? FindTarget(ExperienceOrbEntry orb, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        global::Zenith.Player.Player? current = null;
        if (orb.TargetRuntimeId is { } id)
            foreach (var player in online)
                if (player.RuntimeId == id && CanAttract(player, orb)) { current = player; break; }

        if (current is null) orb.TargetRuntimeId = null;
        if (--orb.LookForTargetIn > 0) return current;
        orb.LookForTargetIn = LookInterval;
        if (current is not null) return current;

        var best = float.MaxValue;
        foreach (var player in online)
        {
            if (!CanAttract(player, orb)) continue;
            var distance = DistanceSquared(orb.Pose, player);
            if (distance >= best) continue;
            best = distance;
            current = player;
        }

        orb.TargetRuntimeId = current?.RuntimeId;
        return current;
    }

    private static bool CanAttract(global::Zenith.Player.Player player, ExperienceOrbEntry orb) =>
        player.IsInGame && !player.IsDead && DistanceSquared(orb.Pose, player) <= TargetRange * TargetRange;

    private static float DistanceSquared(FloorDropMotion pose, global::Zenith.Player.Player player)
    {
        var dx = player.PositionX - pose.X;
        var dy = player.PositionY + HalfEye - pose.Y;
        var dz = player.PositionZ - pose.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Vanilla pull: stronger the nearer the orb is, nothing at the edge of the range.</summary>
    private static FloorDropMotion Pull(FloorDropMotion pose, global::Zenith.Player.Player target)
    {
        var vx = (target.PositionX - pose.X) / TargetRange;
        var vy = (target.PositionY + HalfEye - pose.Y) / TargetRange;
        var vz = (target.PositionZ - pose.Z) / TargetRange;
        var lengthSquared = vx * vx + vy * vy + vz * vz;
        if (lengthSquared >= 1f || lengthSquared <= 0f) return pose;

        var length = MathF.Sqrt(lengthSquared);
        var force = 0.2f * MathF.Pow(1f - length, 2f);
        return pose with
        {
            VelocityX = pose.VelocityX + vx / length * force,
            VelocityY = pose.VelocityY + vy / length * force,
            VelocityZ = pose.VelocityZ + vz / length * force,
        };
    }

    private static bool Touches(ExperienceOrbEntry orb, global::Zenith.Player.Player player) =>
        Aabb.FromCenterSize(orb.Pose.X, orb.Pose.Y, orb.Pose.Z, EntityHitboxes.ItemEntitySize, EntityHitboxes.ItemEntitySize)
            .Intersects(EntityHitboxes.PlayerStanding(player.PositionX, player.PositionY, player.PositionZ));

    /// <summary>Credit and removal in one step: the orb leaves the store before anything else can touch it, so it is collected once.</summary>
    private void Collect(ExperienceOrbEntry orb, global::Zenith.Player.Player player, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _world.ExperienceOrbs.Remove(orb);
        player.AddExperience(orb.Value);
        player.Client.Entity.SendPlayerAttributes(player);
        player.Client.World.SendSoundCue(SoundCue.ExperienceOrbPickup, orb.Pose.X, orb.Pose.Y, orb.Pose.Z);
        foreach (var peer in online)
            if (peer.IsInGame)
                peer.Client.Entity.SendRemoveActor(orb.EntityId);
    }

    /// <summary>Expiry: gone without giving anything.</summary>
    private void Remove(ExperienceOrbEntry orb, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _world.ExperienceOrbs.Remove(orb);
        foreach (var peer in online)
            if (peer.IsInGame)
                peer.Client.Entity.SendRemoveActor(orb.EntityId);
    }

    /// <summary>Tells every in-game peer an orb exists.</summary>
    public static void Announce(IReadOnlyList<global::Zenith.Player.Player> online, ExperienceOrbEntry orb)
    {
        foreach (var peer in online)
            if (peer.IsInGame)
                Describe(peer.Client.Entity, orb);
    }

    /// <summary>The one wire description of an orb, shared by spawn and late join.</summary>
    public static void Describe(IEntityChannel channel, ExperienceOrbEntry orb) =>
        channel.SendAddExperienceOrb(orb.EntityId, orb.Value, orb.Pose.X, orb.Pose.Y, orb.Pose.Z,
            orb.Pose.VelocityX, orb.Pose.VelocityY, orb.Pose.VelocityZ);
}
