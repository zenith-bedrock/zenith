using Xunit;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Property-style check of the ISR move path: whatever random mix of transfers and swaps between the bag, cursor,
/// offhand, armor and the personal craft grid a client sends (valid or not), every item stays somewhere — a request
/// either applies completely or rolls back, and moves never create or destroy items.
/// </summary>
public class InventoryConservationTests
{
    public InventoryConservationTests() => Blocks.EnsureLoaded();

    private static readonly StackId[] Kinds =
    [
        StackId.FromBlock(Blocks.Dirt),
        StackId.FromBlock(Blocks.Stone),
        StackId.FromItem(300, aux: 0),
        StackId.FromItem(300, aux: 1), // same item, other aux: must never merge with aux 0
    ];

    /// <summary>The random requests and the seeded stacks share a few bag slots so most requests have something to move.</summary>
    private const int BusySlots = 5;

    /// <summary>Every stack the player holds, as item-kind → total count.</summary>
    private static Dictionary<StackId, int> Census(Player.Player p, IEnumerable<InventorySlot>? extra = null)
    {
        var census = new Dictionary<StackId, int>();
        void Add(InventorySlot s)
        {
            if (!s.IsEmpty) census[s.Id] = census.GetValueOrDefault(s.Id) + s.Count;
        }

        for (var i = 0; i < PlayerInventory.FullInventorySize; i++) Add(p.Inventory.Get(i));
        for (var i = 0; i < PlayerInventory.ArmorSize; i++) Add(p.Inventory.GetArmor(i));
        for (var i = 0; i < PlayerCraftUi.GridSize; i++) Add(p.Windows.CraftUi.GetGrid(i));
        Add(p.Inventory.Cursor);
        Add(p.Inventory.Offhand);
        foreach (var slot in extra ?? []) Add(slot);
        return census;
    }

    /// <summary>Mostly bag/cursor/offhand/craft grid (where moves succeed); armor now and then (where most are refused).</summary>
    private static InventorySlotReference RandomReference(Random rng, bool withChest = false) => rng.Next(withChest ? 14 : 10) switch
    {
        >= 10 => InventorySlotReference.OpenContainer(rng.Next(ChestStore.DoubleSize)),
        < 4 => InventorySlotReference.Player(rng.Next(BusySlots)),
        4 or 5 => InventorySlotReference.Cursor,
        6 or 7 => InventorySlotReference.Offhand,
        8 => InventorySlotReference.CraftGrid(rng.Next(PlayerCraftUi.GridSize)),
        _ => InventorySlotReference.Armor(rng.Next(PlayerInventory.ArmorSize)),
    };

    /// <summary>Where every stack sits, so a test can tell a request that moved something from one that was refused.</summary>
    private static string Layout(Player.Player p, IEnumerable<InventorySlot>? extra = null)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++) sb.Append(p.Inventory.Get(i)).Append(';');
        for (var i = 0; i < PlayerInventory.ArmorSize; i++) sb.Append(p.Inventory.GetArmor(i)).Append(';');
        for (var i = 0; i < PlayerCraftUi.GridSize; i++) sb.Append(p.Windows.CraftUi.GetGrid(i)).Append(';');
        foreach (var slot in extra ?? []) sb.Append(slot).Append(';');
        return sb.Append(p.Inventory.Cursor).Append(';').Append(p.Inventory.Offhand).ToString();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Random_move_requests_never_create_or_lose_items(int seed) => RunRandomMoves(seed, withChest: false, withDrops: false);

    /// <summary>Same property with a double chest open: its 54 slots are one more place items can sit or move to.</summary>
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void Random_move_requests_with_a_double_chest_open_never_create_or_lose_items(int seed) =>
        RunRandomMoves(seed, withChest: true, withDrops: false);

    /// <summary>
    /// Survival player who also sends Drop and Destroy: a drop must land on the floor in full (what left the player
    /// is exactly what the floor gained), and the Creative-only Destroy must never delete anything in survival.
    /// </summary>
    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    public void Random_requests_with_drops_and_destroys_conserve_items_between_the_player_and_the_floor(int seed) =>
        RunRandomMoves(seed, withChest: false, withDrops: true);

    private static void RunRandomMoves(int seed, bool withChest, bool withDrops)
    {
        var rng = new Random(seed);
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer($"conserve-{seed}");
        var system = fx.CreateInventorySystem();
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        // Everything that is not on the player: the open chest's slots and what has been dropped on the floor.
        IEnumerable<InventorySlot> Chest()
        {
            foreach (var drop in fx.World.FloorDrops.Snapshot()) yield return new InventorySlot(drop.Id, drop.Count);
            if (!withChest) yield break;
            for (var i = 0; i < ChestStore.SingleSize; i++) yield return fx.World.Chests.Get(2, 64, 2, i);
            for (var i = 0; i < ChestStore.SingleSize; i++) yield return fx.World.Chests.Get(3, 64, 2, i);
        }

        if (withChest)
        {
            var south = Blocks.ChestForFacing(Blocks.CardinalSouth);
            fx.World.SetBlock(2, 64, 2, south);
            fx.World.SetBlock(3, 64, 2, south);
            Assert.True(fx.World.Chests.TryEnsure(2, 64, 2));
            Assert.True(fx.World.Chests.TryEnsure(3, 64, 2));
            player.PositionX = 2.5f;
            player.PositionY = 64;
            player.PositionZ = 2.5f;
            Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(3, 64, 2)));
            system.Tick(fx.Clock, fx.Players.Online);
            Assert.Equal(ChestStore.DoubleSize, player.Windows.OpenChest!.Value.SlotCount);
            // A few stacks already in the chest, in both halves.
            Assert.True(fx.World.Chests.TrySet(2, 64, 2, 0, new InventorySlot(Kinds[0], 20)));
            Assert.True(fx.World.Chests.TrySet(3, 64, 2, 1, new InventorySlot(Kinds[3], 7)));
        }

        // Seed a handful of mixed stacks across the bag, the cursor and the offhand.
        for (var i = 0; i < 12; i++)
            Assert.True(player.Inventory.TrySet(rng.Next(BusySlots), Kinds[rng.Next(Kinds.Length)], 1 + rng.Next(40)));
        Assert.True(player.Inventory.TrySet(PlayerInventory.CursorSlot, Kinds[rng.Next(Kinds.Length)], 1 + rng.Next(40)));
        Assert.True(player.Inventory.TrySetOffhand(Kinds[rng.Next(Kinds.Length)], 1 + rng.Next(40)));

        var expected = Census(player, Chest());
        var moved = 0;
        for (var request = 0; request < 150; request++)
        {
            var actions = new List<InventoryStackAction>();
            for (var a = rng.Next(4) == 0 ? 2 : 1; a > 0; a--)
            {
                var from = RandomReference(rng, withChest);
                var to = RandomReference(rng, withChest);
                var roll = rng.Next(withDrops ? 5 : 3);
                actions.Add(roll switch
                {
                    0 => InventoryStackAction.Swap(from, to),
                    3 => InventoryStackAction.Drop(from, rng.Next(0, 65)),
                    4 => InventoryStackAction.Destroy(from, rng.Next(0, 65), default),
                    _ => InventoryStackAction.Transfer(from, to, rng.Next(0, 65)),
                });
            }

            var layoutBefore = Layout(player, Chest());
            Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1000 + request, actions.ToArray())));
            system.Tick(fx.Clock, fx.Players.Online);

            if (Layout(player, Chest()) != layoutBefore) moved++;
            var actual = Census(player, Chest());
            Assert.True(
                expected.Count == actual.Count && expected.All(kv => actual.GetValueOrDefault(kv.Key) == kv.Value),
                $"seed {seed}, request {request}: items were created or lost.");
            for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
                Assert.InRange(player.Inventory.Get(i).Count, 0, PlayerInventory.MaxStack);
        }

        // Guards against a vacuous pass: a good share of the random requests must really have applied.
        if (withDrops) Assert.NotEmpty(fx.World.FloorDrops.Snapshot());
        Assert.True(moved >= 20, $"seed {seed}: only {moved}/150 requests changed anything.");
    }
}
