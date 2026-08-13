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
    private const float ExhaustionThreshold = 4f;
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
                _ = PlayerDamage.Apply(player, _players, online, DamageSource.Starve, 1f);
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
    }

    private static void AccrueExhaustion(Player.Player player)
    {
        if (player.IsSprinting)
            player.Exhaustion += ExhaustionPerSprintTick;

        if (player.Exhaustion < ExhaustionThreshold) return;
        player.Exhaustion -= ExhaustionThreshold;
        player.Hunger = MathF.Max(0f, player.Hunger - 1f);
    }
}
