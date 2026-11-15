using Zenith.Ecs;
using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Moves every pushed actor along its <see cref="Knockback"/> and fades it, for every species the same way: a step the terrain
/// refuses (a wall) ends the push there, as walking into it would. It runs before the species so a push set last tick is
/// replicated by their move batch. It owns the motion only; who gets pushed, and by how much, is <see cref="MobCore.ApplyKnockback"/>
/// from the species' <see cref="KnockbackProfile"/>.
/// </summary>
sealed class KnockbackSystem(World.World world, EntityRuntime stores) : IGameSystem
{
    private const float Negligible = 0.001f;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        foreach (var id in stores.Knockbacks.Entities)
        {
            ref var push = ref stores.Knockbacks.GetRef(id);
            if (push.X == 0f && push.Z == 0f) continue;
            if (!stores.Positions.TryGet(id, out var pos)) continue;

            var x = pos.X + push.X;
            var z = pos.Z + push.Z;
            if (!GroundMobMovement.TryMoveHorizontal(world, pos.X, pos.Y, pos.Z, x, z, out var resolvedY))
            {
                push.X = 0f;
                push.Z = 0f;
                continue;
            }

            ref var p = ref stores.Positions.GetRef(id);
            p.X = x;
            p.Y = resolvedY;
            p.Z = z;
            push.X *= push.DecayPerTick;
            push.Z *= push.DecayPerTick;
            if (MathF.Abs(push.X) < Negligible) push.X = 0f;
            if (MathF.Abs(push.Z) < Negligible) push.Z = 0f;
        }
    }
}
