using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// What liquid does to a survival player's body (ADR §171). With the head under water the breath runs down a tick at a
/// time from <see cref="MaxAirSupply"/>; at <see cref="DrownThreshold"/> the player takes <see cref="DrownDamage"/> and the
/// count restarts from zero, so drowning hurts about once a second. Surfacing refills it quickly. In lava the player takes
/// <see cref="LavaDamage"/> every time the damage window allows. The owner's client is told the air supply when it has
/// moved enough to matter. Creative players are untouched. Movement, buoyancy and the swimming stroke are the client's
/// own; the server only judges the consequences.
/// </summary>
[GameSystem("air-supply")]
sealed class AirSupplySystem(PlayerManager players) : IGameSystem
{
    public const int MaxAirSupply = 300;
    public const int DrownThreshold = -20;
    public const float DrownDamage = 2f;
    public const float LavaDamage = 4f;
    public const int RefillPerTick = 4;
    private const int ReportEvery = 10;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            if (player.GameMode == GameMode.Creative)
            {
                player.AirSupply = MaxAirSupply;
                Report(player);
                continue;
            }

            if (SubmersionProbe.AtFeet(player) == FluidType.Lava && !EffectRules.ImmuneToFire(player))
                PlayerDamage.ApplyCore(player, players, DamageSource.Lava, LavaDamage, clock.CurrentTick).Conclude(online);
            if (player.IsDead) continue;

            player.AirSupply = Breathe(player.AirSupply, SubmersionProbe.AtHead(player) == FluidType.Water && !EffectRules.BreathesUnderWater(player), out var drowning);
            if (drowning)
                PlayerDamage.ApplyCore(player, players, DamageSource.Drown, DrownDamage, clock.CurrentTick).Conclude(online);

            Report(player);
        }
    }

    /// <summary>One tick of breathing for any body: the new air supply, and whether it just ran out and must take drowning damage.</summary>
    public static int Breathe(int air, bool headUnderWater, out bool drowning)
    {
        drowning = false;
        if (headUnderWater)
        {
            if (--air > DrownThreshold) return air;
            drowning = true;
            return 0;
        }

        return air < MaxAirSupply ? Math.Min(MaxAirSupply, Math.Max(0, air) + RefillPerTick) : air;
    }

    private static void Report(Player.Player player)
    {
        var told = player.Replicated.AirSupply;
        var air = player.AirSupply;
        var atEdge = (air == MaxAirSupply) != (told == MaxAirSupply);
        if (!atEdge && Math.Abs(air - told) < ReportEvery) return;

        player.Replicated.AirSupply = air;
        player.Client.Entity.SendAirSupply((ulong)player.RuntimeId, (short)Math.Max(air, 0), MaxAirSupply);
    }
}
