using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Policy;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Geometry;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// One explosion, the vanilla way (plan 004 items 3 and 4). It has two halves that share a centre and a power.
/// <para/>
/// <b>Blocks.</b> A 16 x 16 x 16 grid of rays leaves the centre; each starts with <c>power * (0.7 to 1.3)</c> and loses
/// 0.225 every 0.3 blocks, plus <c>(blast resistance + 0.3) * 0.3</c> for every block it crosses. Whatever block it still has
/// strength left for is destroyed, so obsidian, water and lava stop a ray where stone does not, and a wall shades what lies
/// behind it. Each destroyed block drops its items with chance <c>1 / power</c>. Every cell is asked of the environment policies
/// (cause <see cref="EnvironmentCause.Explosion"/>) and a vetoed cell is simply left standing.
/// <para/>
/// <b>Bodies.</b> Anything with health within <c>2 * power</c> blocks takes <c>floor((i^2 + i) / 2 * 7 * 2 * power + 1)</c>
/// where <c>i = (1 - distance / (2 * power)) * exposure</c> and exposure is the share of the body a straight line to the centre
/// reaches without crossing a solid block. It is also pushed away, by <c>i</c>.
/// </summary>
sealed class Explosion(
    World.World world, PlayerManager players, EntityRuntime stores, EnvironmentChangePolicies policies, Random random)
{
    private const int GridMax = 15;
    private const float RayStep = 0.3f;
    private const float RayDecay = 0.75f * RayStep;
    private const float ExposureStep = 0.25f;
    private const float Skin = 0.01f;

    /// <summary>Box used for mobs: the ECS carries no size per species. A person-sized box is right for most of the roster.</summary>
    private const float MobWidth = 0.6f;
    private const float MobHeight = 1.8f;

    /// <summary>Strength of the push a mob gets at full impact (the knockback component fades by half each tick, so about twice this far).</summary>
    private const float MobPushAtFullImpact = 0.6f;

    private readonly HashSet<(int X, int Y, int Z)> _cells = [];
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _destroyed = [];

    /// <summary>The cells that went to air in the last <see cref="Detonate"/>, for the clients.</summary>
    public IReadOnlyList<(int X, int Y, int Z, int BlockRuntimeId)> Destroyed => _destroyed;

    /// <summary>Set once at composition: how a hit on a mob finds its species. Without it only blocks and players are affected.</summary>
    public DamageDispatch? Mobs { get; set; }

    public void Detonate(
        float x, float y, float z, float power, IReadOnlyList<Player.Player> online, ulong tick, EntityId? excluding = null)
    {
        DestroyBlocks(x, y, z, power, online, tick);
        HurtBodies(x, y, z, power, online, tick, excluding);
    }

    // ---- blocks ----------------------------------------------------------------------------------------------------

    private void DestroyBlocks(float cx, float cy, float cz, float power, IReadOnlyList<Player.Player> online, ulong tick)
    {
        _cells.Clear();
        _destroyed.Clear();
        for (var i = 0; i <= GridMax; i++)
        for (var j = 0; j <= GridMax; j++)
        for (var k = 0; k <= GridMax; k++)
        {
            if (i != 0 && i != GridMax && j != 0 && j != GridMax && k != 0 && k != GridMax) continue; // the surface of the cube only

            var dx = i / (float)GridMax * 2f - 1f;
            var dy = j / (float)GridMax * 2f - 1f;
            var dz = k / (float)GridMax * 2f - 1f;
            var length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            dx = dx / length * RayStep;
            dy = dy / length * RayStep;
            dz = dz / length * RayStep;
            CastRay(cx, cy, cz, dx, dy, dz, power * (0.7f + random.NextSingle() * 0.6f));
        }

        var chance = 1f / power;
        foreach (var (x, y, z) in _cells)
        {
            if (!world.TryGetBlock(x, y, z, out var current) || Blocks.IsAir(current)) continue;
            if (!policies.Allows(EnvironmentCause.Explosion, tick, x, y, z, current, World.World.AirRuntimeId)) continue;
            if (!world.TrySetBlock(x, y, z, World.World.AirRuntimeId)) continue;

            _destroyed.Add((x, y, z, World.World.AirRuntimeId));
            if (random.NextSingle() < chance && !Blocks.IsFire(current))
                BlockSpill.Drop(world, players, online, x, y, z, current, random);
        }
    }

    private void CastRay(float x, float y, float z, float dx, float dy, float dz, float strength)
    {
        for (; strength > 0f; strength -= RayDecay)
        {
            var (bx, by, bz) = ((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z));
            if (!world.TryGetBlock(bx, by, bz, out var block)) return; // an unloaded column is the edge of what can be known

            if (!Blocks.IsAir(block))
            {
                strength -= (Blocks.BlastResistanceOf(block) + 0.3f) * RayStep;
                if (strength > 0f) _cells.Add((bx, by, bz));
            }

            x += dx;
            y += dy;
            z += dz;
        }
    }

    // ---- bodies ----------------------------------------------------------------------------------------------------

    private void HurtBodies(
        float cx, float cy, float cz, float power, IReadOnlyList<Player.Player> online, ulong tick, EntityId? excluding)
    {
        var radius = power * 2f;

        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var box = EntityHitboxes.PlayerStanding(player.PositionX, player.PositionY, player.PositionZ);
            if (!TryImpact(cx, cy, cz, radius, player.PositionX, player.PositionY, player.PositionZ, box, out var impact, out var dirX, out var dirY, out var dirZ))
                continue;

            var result = PlayerDamage.ApplyCore(player, players, DamageSource.Explosion, DamageFor(impact, radius), tick, dirX, dirZ);
            result.Conclude(online);
            if (result.Applied && !player.IsDead)
                player.Client.Entity.SendKnockback((ulong)player.RuntimeId, dirX * impact, dirY * impact + impact * 0.5f, dirZ * impact);
        }

        if (Mobs is not { } dispatch) return;
        foreach (var id in Query.With(stores.Health, stores.Positions))
        {
            if (id == excluding || stores.Corpses.Has(id)) continue;
            var pos = stores.Positions.TryGet(id, out var p0) ? p0 : default;
            var box = Aabb.FromCenterSize(pos.X, pos.Y, pos.Z, MobWidth, MobHeight);
            if (!TryImpact(cx, cy, cz, radius, pos.X, pos.Y, pos.Z, box, out var impact, out var dirX, out _, out var dirZ)) continue;

            var applied = dispatch.TryApplyDamage(id, DamageSource.Explosion, DamageFor(impact, radius), online, tick);
            if (applied && stores.Entities.IsAlive(id) && stores.Knockbacks.Has(id))
            {
                ref var push = ref stores.Knockbacks.GetRef(id);
                push.X = dirX * impact * MobPushAtFullImpact;
                push.Z = dirZ * impact * MobPushAtFullImpact;
            }
        }
    }

    /// <summary>Vanilla's damage curve, for a body that takes <paramref name="impact"/> of a blast of reach <paramref name="radius"/>.</summary>
    internal static float DamageFor(float impact, float radius) => MathF.Floor((impact * impact + impact) / 2f * 7f * radius + 1f);

    private bool TryImpact(
        float cx, float cy, float cz, float radius, float bx, float by, float bz, Aabb box,
        out float impact, out float dirX, out float dirY, out float dirZ)
    {
        dirX = bx - cx;
        dirY = by + (box.MaxY - box.MinY) * 0.5f - cy;
        dirZ = bz - cz;
        impact = 0f;
        var feet = MathF.Sqrt(dirX * dirX + (by - cy) * (by - cy) + dirZ * dirZ);
        if (feet >= radius) return false;

        var length = MathF.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        if (length > 0f) (dirX, dirY, dirZ) = (dirX / length, dirY / length, dirZ / length);
        else (dirX, dirY, dirZ) = (0f, 1f, 0f);

        var exposure = Exposure(cx, cy, cz, box);
        impact = (1f - feet / radius) * exposure;
        return impact > 0f;
    }

    /// <summary>The share of a grid of points over the box that a straight line reaches the centre from without crossing a solid block.</summary>
    private float Exposure(float cx, float cy, float cz, Aabb box)
    {
        var (w, h, d) = (box.MaxX - box.MinX, box.MaxY - box.MinY, box.MaxZ - box.MinZ);
        var (sx, sy, sz) = (1f / (w * 2f + 1f), 1f / (h * 2f + 1f), 1f / (d * 2f + 1f));
        var offsetX = (1f - MathF.Floor(1f / sx) * sx) / 2f;
        var offsetZ = (1f - MathF.Floor(1f / sz) * sz) / 2f;
        int clear = 0, total = 0;
        for (var fx = 0f; fx <= 1f; fx += sx)
        for (var fy = 0f; fy <= 1f; fy += sy)
        for (var fz = 0f; fz <= 1f; fz += sz)
        {
            total++;
            var px = box.MinX + w * fx + offsetX;
            var py = box.MinY + h * fy;
            var pz = box.MinZ + d * fz + offsetZ;
            if (LineIsClear(px, py, pz, cx, cy, cz)) clear++;
        }

        return total == 0 ? 0f : (float)clear / total;
    }

    private bool LineIsClear(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        // A point exactly on a block face (a body's feet on the floor, a blast at ground level) belongs to the open cell above it.
        (y0, y1) = (y0 + Skin, y1 + Skin);
        var (dx, dy, dz) = (x1 - x0, y1 - y0, z1 - z0);
        var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(dx * dx + dy * dy + dz * dz) / ExposureStep));
        for (var s = 1; s < steps; s++)
        {
            var t = s / (float)steps;
            if (world.TryGetBlock((int)MathF.Floor(x0 + dx * t), (int)MathF.Floor(y0 + dy * t), (int)MathF.Floor(z0 + dz * t), out var block) &&
                Blocks.BlocksMovement(block))
                return false;
        }

        return true;
    }
}
