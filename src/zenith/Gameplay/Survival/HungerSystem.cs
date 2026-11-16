using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Survival;

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

    private const float RegenHungerThreshold = 18f;
    private const float RegenExhaustionCost = 6f;
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
            if (!player.IsInGame || player.IsDead)
            {
                player.FoodUse = null;
                while (player.Intents.TryConsumeFoodUse(out _)) { }
                continue;
            }

            var hungerBefore = player.Hunger;
            var saturationBefore = player.Saturation;
            TryEat(player, clock.CurrentTick, online);

            if (player.GameMode == GameMode.Creative)
            {
                if (player.Hunger != hungerBefore || player.Saturation != saturationBefore)
                    player.Client.Entity.SendPlayerAttributes(player);
                continue;
            }

            AccrueExhaustion(player);

            // Starvation has a health floor of 1 (vanilla's non-Hard-difficulty default — Zenith has
            // no difficulty concept to gate this on, so it defaults to the common case: starving
            // never kills on its own, it just leaves the player critically low).
            if (player.Hunger <= 0f && player.Health > 1f && clock.CurrentTick % StarveDamageIntervalTicks == 0)
            {
                // PlayerDamage already sends attributes/relays health for both outcomes.
                PlayerDamage.ApplyCore(player, _players, DamageSource.Starve, 1f, clock.CurrentTick).Conclude(online);
                continue;
            }

            if (player.Hunger >= RegenHungerThreshold && player.Health < player.MaxHealth &&
                player.Level.Rules.NaturalRegeneration &&
                clock.CurrentTick % RegenIntervalTicks == 0)
            {
                player.Heal(1f);
                // Every vanilla-parity natural heal costs exhaustion, same as sprinting/mining/damage
                // — otherwise a well-fed player heals indefinitely at no food cost.
                player.Exhaustion += RegenExhaustionCost;
                player.Client.Entity.SendPlayerAttributes(player);
                PlayerVisibility.RelayHealth(player, online);
                continue;
            }

            // Food/saturation changes are immediate. Exhaustion (also accrued by mining and
            // damage outside this system) is refreshed at most once per second while nonzero.
            if (player.Hunger != hungerBefore || player.Saturation != saturationBefore ||
                (player.Exhaustion != 0f && clock.CurrentTick % GameClock.TicksPerSecond == 0))
                player.Client.Entity.SendPlayerAttributes(player);
        }
    }

    private void TryEat(Player.Player player, ulong tick, IReadOnlyList<Player.Player> online)
    {
        while (player.Intents.TryConsumeFoodUse(out var request))
        {
            if (request.Cancel) { SetFoodUse(player, null, online); continue; }
            var slot = request.Slot < 0 ? player.SelectedHotbarSlot : request.Slot;
            if (!PlayerInventory.IsValidHotbarSlot(slot) || player.SelectedHotbarSlot != slot) continue;
            var stack = player.Inventory.Get(slot);
            if (stack.IsEmpty || (request.ExpectedStack is { } expected && expected != stack) ||
                !FoodItems.TryGet(_itemPalette, stack.Id, out var food) ||
                (player.Hunger >= 20f && !food.AlwaysEdible)) continue;
            if (player.FoodUse is { } active && active.Slot == slot && active.Stack == stack) continue;
            SetFoodUse(player, new FoodUseState(slot, stack, tick), online);
        }
        if (player.FoodUse is not { } use) return;
        if (player.SelectedHotbarSlot != use.Slot || player.Inventory.Get(use.Slot) != use.Stack ||
            !FoodItems.TryGet(_itemPalette, use.Stack.Id, out var definition) ||
            (player.Hunger >= 20f && !definition.AlwaysEdible))
        {
            SetFoodUse(player, null, online);
            return;
        }
        if (tick - use.StartedTick < (ulong)definition.UseTicks) return;
        SetFoodUse(player, null, online);
        if (player.GameMode != GameMode.Creative && !ConsumeFood(player, use, definition, online)) return;
        player.Hunger = MathF.Min(20f, player.Hunger + definition.Nutrition);
        player.Saturation = MathF.Min(player.Hunger, player.Saturation + definition.Saturation);
        if (definition.ClearsEffects)
            player.Effects.SubmitEffect(EffectIntent.Clear);
    }

    private static void SetFoodUse(Player.Player player, FoodUseState? use, IReadOnlyList<Player.Player> online)
    {
        var changed = player.FoodUse.HasValue != use.HasValue;
        player.FoodUse = use;
        if (!changed) return;
        foreach (var peer in online)
            if (peer.IsInGame)
                peer.Client.Entity.SendActorFlags((ulong)player.RuntimeId, player.IsSneaking, player.IsSprinting, use.HasValue, player.IsSwimming);
    }

    private bool ConsumeFood(Player.Player player, FoodUseState use, FoodDefinition food, IReadOnlyList<Player.Player> online)
    {
        var snapshot = player.Inventory.CaptureSnapshot();
        player.Inventory.TrySet(use.Slot, use.Stack.Id, use.Stack.Count - 1);
        if (food.Remainder is { } remainder)
        {
            var id = StackId.FromItem(_itemPalette.Require(remainder));
            if (use.Stack.Count == 1)
                player.Inventory.TrySet(use.Slot, id, 1);
            else if (!player.Inventory.TryAdd(id))
            {
                var x = (int)MathF.Floor(player.PositionX);
                var y = (int)MathF.Floor(player.PositionY);
                var z = (int)MathF.Floor(player.PositionZ);
                if (!FloorDropFanout.TryDeposit(player.Level, _players, online, x, y, z, id, 1))
                {
                    player.Inventory.RestoreSnapshot(snapshot);
                    return false;
                }
            }
        }
        player.Client.Inventory.SendInventoryContent(player.Inventory);
        player.Level.Persistence.PersistInventory(player);
        return true;
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
