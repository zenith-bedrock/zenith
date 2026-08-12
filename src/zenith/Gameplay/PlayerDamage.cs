using Zenith.Player;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Gameplay;

/// <summary>One authoritative Player damage transition shared by concrete gameplay causes.</summary>
static class PlayerDamage
{
    public static bool Apply(Player.Player player, PlayerManager players, IReadOnlyList<Player.Player> online, DamageSource source, float amount)
    {
        var result = player.ApplyDamage(source, amount);
        if (!result.WasApplied) return false;
        var entity = player.Session.Protocol.Entity;
        var rid = (ulong)player.RuntimeId;
        if (!result.CausedDeath)
        {
            entity.SendDefaultAttributes(rid, player.Health, player.Hunger);
            PlayerVisibility.RelayHealth(player, online);
            return true;
        }
        var world = player.Session.Context.World;
        if (player.OpenChest.HasValue) ChestLidFanout.ReleaseOpener(online, world, player);
        if (!player.TryFinalizeDeath()) throw new InvalidOperationException("A newly lethal HealthState did not finalize its death transition.");
        if (player.GameMode != GameMode.Creative) _ = FloorDropFanout.TryDropDeathLoot(world, players, online, player);
        entity.SendDefaultAttributes(rid, player.Health, player.Hunger);
        PlayerVisibility.RelayHealth(player, online);
        entity.SendDeathInfo(player.DeathCause);
        entity.SendRespawnSearching(player.PositionX, player.PositionY + Blocks.PlayerEyeHeight, player.PositionZ, rid);
        return true;
    }
}
