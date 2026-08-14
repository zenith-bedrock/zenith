using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// First concrete food/hunger slice (Phase XI.1). Owns the authoritative <c>Player.Hunger</c> /
/// <c>Player.Exhaustion</c> transitions: eating a held food stack, sprint exhaustion accrual,
/// starvation damage and well-fed regeneration. No AttributeSystem, no item-component pipeline —
/// food identity comes from the small <see cref="FoodItems"/> table.
/// </summary>
sealed class HungerSystem : IGameSystem
{
    private const float ExhaustionPerSprintTick = 0.1f;

    /// <summary>
    /// Phase XXV — exhaustion sources beyond sprint (previously the only one that existed at all,
    /// a real gap from the earlier cross-reference audit). Values are vanilla-adjacent, confirmed
    /// against a reference implementation for the damage figure; walking's own much smaller
    /// per-block cost is deliberately deferred — it needs distance-moved tracking that doesn't exist
    /// yet in <see cref="MovementSystem"/>, unlike mining (one call per completed break) and damage
    /// (already has a single funnel in <see cref="PlayerDamage"/>).
    /// </summary>
    internal const float MiningExhaustionPerBlock = 0.005f;
    internal const float DamageExhaustion = 0.1f;

    private const float ExhaustionThreshold = 4f;

    /// <summary>
    /// Phase XXV — vanilla's "well-fed" cushion: an exhaustion-threshold crossing depletes
    /// saturation before it ever touches <see cref="Player.Player.Hunger"/>. Restore ratio is a
    /// single vanilla-adjacent constant applied to every food's nutrition value, not a per-food
    /// saturation-modifier table (vanilla has one per food; this is a deliberate simplification —
    /// see the Phase XXV findings doc).
    /// </summary>
    private const float SaturationRestoreRatio = 0.5f;

    private const float RegenHungerThreshold = 18f;
    private const ulong StarveDamageIntervalTicks = 80; // 4s @ 20 TPS, vanilla-parity cadence.
    private const ulong RegenIntervalTicks = 80;

    private readonly ItemPalette _itemPalette;
    private readonly PlayerManager _players;

    public HungerSystem(ItemPalette itemPalette, PlayerManager players)
    {
        _itemPalette = itemPalette;
        _players = players;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;

            var hungerBefore = player.Hunger;
            TryEat(player);

            if (player.GameMode == GameMode.Creative)
            {
                if (player.Hunger != hungerBefore) player.Session.Protocol.Entity.SendPlayerAttributes(player);
                continue;
            }

            AccrueExhaustion(player);

            if (player.Hunger <= 0f && clock.CurrentTick % StarveDamageIntervalTicks == 0)
            {
                // PlayerDamage.Apply already sends attributes/relays health for both outcomes.
                _ = PlayerDamage.Apply(player, _players, online, DamageSource.Starve, 1f, clock.CurrentTick);
                continue;
            }

            if (player.Hunger >= RegenHungerThreshold && player.Health < player.MaxHealth &&
                clock.CurrentTick % RegenIntervalTicks == 0)
            {
                player.Heal(1f);
                player.Session.Protocol.Entity.SendPlayerAttributes(player);
                PlayerVisibility.RelayHealth(player, online);
                continue;
            }

            if (player.Hunger != hungerBefore) player.Session.Protocol.Entity.SendPlayerAttributes(player);
        }
    }

    private void TryEat(Player.Player player)
    {
        if (!player.TryConsumeEatIntent()) return;
        if (player.Hunger >= 20f) return;

        var slot = player.SelectedHotbarSlot;
        if (!PlayerInventory.IsValidHotbarSlot(slot)) return;

        var stack = player.Inventory.Get(slot);
        if (stack.Count <= 0) return;
        if (!FoodItems.TryGetNutrition(_itemPalette, stack.Id, out var nutrition)) return;

        if (player.GameMode != GameMode.Creative)
        {
            if (!player.Inventory.TryConsume(stack.Id, 1)) return;
            player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
            player.Session.Context.World.PersistInventory(player);
        }

        player.Hunger = MathF.Min(20f, player.Hunger + nutrition);
        // Vanilla caps saturation at the current food level — never lets a big meal "bank" more
        // cushion than the hunger bar it's attached to.
        player.Saturation = MathF.Min(player.Hunger, player.Saturation + nutrition * SaturationRestoreRatio);
    }

    private static void AccrueExhaustion(Player.Player player)
    {
        if (player.IsSprinting)
            player.Exhaustion += ExhaustionPerSprintTick;

        if (player.Exhaustion < ExhaustionThreshold) return;
        player.Exhaustion -= ExhaustionThreshold;

        if (player.Saturation > 0f)
        {
            player.Saturation = MathF.Max(0f, player.Saturation - 1f);
            return;
        }

        player.Hunger = MathF.Max(0f, player.Hunger - 1f);
    }
}
