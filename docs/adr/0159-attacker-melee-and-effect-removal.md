# ADR §159 — Attacker-owned melee and effect removal

Player melee damage is resolved from the attacker's authoritative held item and the
item palette, for both mob and player targets. A species' outgoing AttackDamage is
never reused as damage dealt to that species. The first table covers bare hand,
tiered swords/axes/pickaxes/shovels and melee trident, using the inspected PocketMine
item facts. Criticals, enchantments, special mace/spear behavior, attack wear and
Strength/Weakness are separate work; no all-weapon-parity claim is made.

Ordinary armor does not mitigate Fall, separately from any future enchantment-based
protection. Effect ClearAll sends one MobEffect Remove for each previously active
type after the authoritative clear, matching the existing expiry projection.

No generic damage framework is introduced. Test equal attacks across target species,
hand versus weapon, PvP/mob resolution, armored falling, and effect removal packets.

Reference facts: PocketMine-MP's [ToolTier](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/ToolTier.php),
[Sword](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/Sword.php),
[Axe](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/Axe.php),
[Pickaxe](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/Pickaxe.php),
[Shovel](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/Shovel.php),
and [Trident](https://github.com/pmmp/PocketMine-MP/blob/stable/src/item/Trident.php).
Only damage values and relationships informed the C# implementation.
