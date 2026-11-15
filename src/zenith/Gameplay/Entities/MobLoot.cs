using Zenith.Gameplay.Loot;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Turns a kill into floor drops: the <see cref="LootTable"/> decides what and how many (it knows nothing of the world), and
/// this places each award on the floor with a small pop. It is the only seam between generation and the floor-drop store.
/// </summary>
static class MobLoot
{
    /// <summary>The loot context of a kill: a melee hit from a player who is still online counts as a player kill.</summary>
    public static LootContext ContextOf(in DamageSource source, IReadOnlyList<Player.Player> online)
    {
        if (source.Cause != DamageCause.Melee || source.OwnerRuntimeId is not { } owner) return new LootContext(false);
        foreach (var player in online)
            if (player.RuntimeId == owner) return new LootContext(true);
        return new LootContext(false);
    }

    /// <summary>
    /// Rolls <paramref name="table"/> and drops every award at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).
    /// Loot capacity never vetoes a kill (ADR §135): an award the floor cannot take is dropped silently.
    /// </summary>
    public static int Drop(
        LootTable table, LootContext context, Random random, World.World world, PlayerManager players,
        IReadOnlyList<Player.Player> online, float x, float y, float z, ItemPalette palette)
    {
        var dropped = 0;
        foreach (var award in table.Roll(context, random))
        {
            var id = StackId.FromItem(palette.Require(award.Item));
            var pop = new FloorDropMotion(
                x, y + 0.3f, z,
                (random.NextSingle() - 0.5f) * 0.2f, 0.2f, (random.NextSingle() - 0.5f) * 0.2f);
            if (FloorDropFanout.TryDepositBatch(
                    world, players, online, (int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z),
                    [new FloorDropFanout.DepositRequest(id, award.Count)], launch: pop))
                dropped++;
        }

        return dropped;
    }
}
