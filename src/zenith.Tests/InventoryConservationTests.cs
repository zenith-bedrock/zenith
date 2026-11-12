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
    private static Dictionary<StackId, int> Census(Player.Player p)
    {
        var census = new Dictionary<StackId, int>();
        void Add(InventorySlot s)
        {
            if (!s.IsEmpty) census[s.Id] = census.GetValueOrDefault(s.Id) + s.Count;
        }

        for (var i = 0; i < PlayerInventory.FullInventorySize; i++) Add(p.Inventory.Get(i));
        for (var i = 0; i < PlayerInventory.ArmorSize; i++) Add(p.Inventory.GetArmor(i));
        for (var i = 0; i < PlayerCraftUi.GridSize; i++) Add(p.CraftUi.GetGrid(i));
        Add(p.Inventory.Cursor);
        Add(p.Inventory.Offhand);
        return census;
    }

    /// <summary>Mostly bag/cursor/offhand/craft grid (where moves succeed); armor now and then (where most are refused).</summary>
    private static InventorySlotReference RandomReference(Random rng) => rng.Next(10) switch
    {
        < 4 => InventorySlotReference.Player(rng.Next(BusySlots)),
        4 or 5 => InventorySlotReference.Cursor,
        6 or 7 => InventorySlotReference.Offhand,
        8 => InventorySlotReference.CraftGrid(rng.Next(PlayerCraftUi.GridSize)),
        _ => InventorySlotReference.Armor(rng.Next(PlayerInventory.ArmorSize)),
    };

    /// <summary>Where every stack sits, so a test can tell a request that moved something from one that was refused.</summary>
    private static string Layout(Player.Player p)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++) sb.Append(p.Inventory.Get(i)).Append(';');
        for (var i = 0; i < PlayerInventory.ArmorSize; i++) sb.Append(p.Inventory.GetArmor(i)).Append(';');
        for (var i = 0; i < PlayerCraftUi.GridSize; i++) sb.Append(p.CraftUi.GetGrid(i)).Append(';');
        return sb.Append(p.Inventory.Cursor).Append(';').Append(p.Inventory.Offhand).ToString();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Random_move_requests_never_create_or_lose_items(int seed)
    {
        var rng = new Random(seed);
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer($"conserve-{seed}");
        player.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        // Seed a handful of mixed stacks across the bag, the cursor and the offhand.
        for (var i = 0; i < 12; i++)
            Assert.True(player.Inventory.TrySet(rng.Next(BusySlots), Kinds[rng.Next(Kinds.Length)], 1 + rng.Next(40)));
        Assert.True(player.Inventory.TrySet(PlayerInventory.CursorSlot, Kinds[rng.Next(Kinds.Length)], 1 + rng.Next(40)));
        Assert.True(player.Inventory.TrySetOffhand(Kinds[rng.Next(Kinds.Length)], 1 + rng.Next(40)));

        var system = fx.CreateInventorySystem();
        var expected = Census(player);
        var moved = 0;
        for (var request = 0; request < 150; request++)
        {
            var actions = new List<InventoryStackAction>();
            for (var a = rng.Next(4) == 0 ? 2 : 1; a > 0; a--)
            {
                var from = RandomReference(rng);
                var to = RandomReference(rng);
                actions.Add(rng.Next(3) == 0
                    ? InventoryStackAction.Swap(from, to)
                    : InventoryStackAction.Transfer(from, to, rng.Next(0, 65)));
            }

            var layoutBefore = Layout(player);
            Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1000 + request, actions.ToArray())));
            system.Tick(fx.Clock, fx.Players.Online);

            if (Layout(player) != layoutBefore) moved++;
            var actual = Census(player);
            Assert.True(
                expected.Count == actual.Count && expected.All(kv => actual.GetValueOrDefault(kv.Key) == kv.Value),
                $"seed {seed}, request {request}: items were created or lost.");
            for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
                Assert.InRange(player.Inventory.Get(i).Count, 0, PlayerInventory.MaxStack);
        }

        // Guards against a vacuous pass: a good share of the random requests must really have applied.
        Assert.True(moved >= 20, $"seed {seed}: only {moved}/150 requests changed anything.");
    }
}
