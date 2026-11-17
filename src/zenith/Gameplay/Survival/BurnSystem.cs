using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Survival;

/// <summary>Players on fire (plan 004 item 8): contact with fire or lava lights them, water puts them out, and the flames hurt once a second. Creative players never burn.</summary>
sealed class BurnSystem(PlayerManager players, World.World world) : IGameSystem
{
    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var before = player.BurnTicks;
            if (player.GameMode == GameMode.Creative)
                player.BurnTicks = 0;
            else
            {
                var contact = Burning.ContactAt(world, player.PositionX, player.PositionY, player.PositionZ, alight: before > 0);
                player.BurnTicks = Burning.Step(before, contact, clock.CurrentTick, out var hurt);
                if (hurt) PlayerDamage.ApplyCore(player, players, DamageSource.Fire, Burning.Damage, clock.CurrentTick).Conclude(online);
            }

            if ((before > 0) == (player.BurnTicks > 0)) continue;
            foreach (var peer in online)
                if (peer.IsInGame)
                    peer.Client.Entity.SendActorFlags(
                        (ulong)player.RuntimeId, player.IsSneaking, player.IsSprinting, player.FoodUse.HasValue, player.IsSwimming, player.BurnTicks > 0);
        }
    }
}
