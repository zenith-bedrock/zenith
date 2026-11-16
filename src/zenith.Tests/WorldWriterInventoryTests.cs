using System.Reflection;
using Xunit;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// A ratchet over who may change a cell of the world (plan 002, phase 0). Every production method that calls a cell-mutating member
/// is listed with why it may: it is a player edit that already passes the block-edit policies, it is an environment-driven writer
/// and names the cause it will be judged under, or it is the world's own construction and storage. Adding a writer, or a new write
/// path inside a listed type, fails here until someone decides which it is.
///
/// The scan reads IL, so it sees real calls (lambdas and async included) and method pointers, and ignores comments. It cannot see
/// reflection. It proves that no unlisted code writes a cell; whether a listed "policed" writer really consults the policies is
/// shown by the behavioural tests named next to each entry, not by this scan.
/// </summary>
public sealed class WorldWriterInventoryTests
{
    private enum Kind
    {
        /// <summary>A player's edit; judged by <c>IBlockEditPolicy</c> before any effect.</summary>
        PlayerEditPoliced,

        /// <summary>Changes the world with no player; will be judged by the environment policy under the named cause.</summary>
        Environment,

        /// <summary>Builds or stores the world itself (generation, loading, the write API and its backing storage). Not gameplay.</summary>
        WorldInternals
    }

    private sealed record Entry(Kind Kind, string Why);

    /// <summary>(type full name, method name) of every allowed caller of a cell-mutating member.</summary>
    private static readonly Dictionary<(string Type, string Method), Entry> CellWriters = new()
    {
        // Player edits: policed. Proven by BlockEditPolicyTests, ProtectModuleTests and ProtectInteractionTests.
        [("Zenith.Gameplay.WorldInteraction.BlockEditSystem", "TryPlace")] = new(Kind.PlayerEditPoliced, "place"),
        [("Zenith.Gameplay.WorldInteraction.BlockEditSystem", "TryBreak")] = new(Kind.PlayerEditPoliced, "break"),
        [("Zenith.Gameplay.WorldInteraction.BlockEditSystem", "CommitExtraWrites")] = new(Kind.PlayerEditPoliced, "coupled cells of a place, break or door use, planned and judged first"),
        [("Zenith.Gameplay.WorldInteraction.Fluids.BucketSystem", "TryScoop")] = new(Kind.PlayerEditPoliced, "bucket scoop, judged as a break"),
        [("Zenith.Gameplay.WorldInteraction.Fluids.BucketSystem", "TryPour")] = new(Kind.PlayerEditPoliced, "bucket pour, judged as a place"),

        // Environment writers: not yet policed. Each names its cause in plan 002.
        [("Zenith.Gameplay.Entities.CreeperSystem", "BreakBlocksInRadius")] = new(Kind.Environment, "Explosion"),
        [("Zenith.Gameplay.WorldInteraction.Fluids.FluidFlow", "Write")] = new(Kind.Environment, "FluidFlow"),
        [("Zenith.Gameplay.WorldInteraction.Fire.FireSpread", "Write")] = new(Kind.Environment, "Fire"),
        [("Zenith.Gameplay.WorldInteraction.GravitySystem", "BeginFall")] = new(Kind.Environment, "Gravity (origin)"),
        [("Zenith.Gameplay.WorldInteraction.GravitySystem", "Land")] = new(Kind.Environment, "Gravity (landing)"),
        [("Zenith.Gameplay.WorldInteraction.BlockUpdateSystem", "BreakUnsupported")] = new(Kind.Environment, "SupportLoss (break and drop)"),
        [("Zenith.Gameplay.WorldInteraction.BlockUpdateSystem", "Evaluate")] = new(Kind.Environment, "SupportLoss (neighbour-driven reshape)"),
        [("Zenith.Gameplay.WorldInteraction.RandomTicks.RandomTickContext", "TrySet")] = new(Kind.Environment, "per behaviour: see RandomTickWriters"),

        // The world itself.
        [("Zenith.World.World", "SetBlock")] = new(Kind.WorldInternals, "setup-time write API"),
        [("Zenith.World.World", "TrySetBlock")] = new(Kind.WorldInternals, "the write API"),
        [("Zenith.World.World", "BuildColumn")] = new(Kind.WorldInternals, "terrain generation installs generated sections"),
        [("Zenith.World.ColumnCodec", "Decode")] = new(Kind.WorldInternals, "loading a saved column"),
        [("Zenith.World.WorldColumn", "Set")] = new(Kind.WorldInternals, "column storage"),
    };

    /// <summary>
    /// The random-tick behaviours that write through <c>RandomTickContext.TrySet</c>, with the cause each carries. The context does
    /// not know its behaviour, so the cause is attributed here, one entry per behaviour.
    /// </summary>
    private static readonly Dictionary<string, string> RandomTickWriters = new()
    {
        ["Zenith.Gameplay.WorldInteraction.RandomTicks.SaplingGrowthBehavior"] = "Growth (a sapling's age, then a whole tree as one planned unit)",
        ["Zenith.Gameplay.WorldInteraction.RandomTicks.LeavesDecayBehavior"] = "Decay",
        ["Zenith.Gameplay.WorldInteraction.RandomTicks.GrassSpreadBehavior"] = "Spread",
        ["Zenith.Gameplay.WorldInteraction.RandomTicks.LavaIgnitionBehavior"] = "Fire",
    };

    private static bool IsCellMutator(MethodBase method) =>
        (method.DeclaringType == typeof(World.World) && method.Name is nameof(World.World.TrySetBlock) or nameof(World.World.SetBlock)) ||
        (method.DeclaringType == typeof(WorldColumn) && method.Name is nameof(WorldColumn.Set) or nameof(WorldColumn.SetSection)) ||
        (method.DeclaringType == typeof(Section) && method.Name is nameof(Section.Set));

    private static bool IsRandomTickWrite(MethodBase method) =>
        method.DeclaringType == typeof(RandomTickContext) && method.Name == nameof(RandomTickContext.TrySet);

    [Fact]
    public void Every_method_that_writes_a_cell_is_classified_and_every_classification_is_still_used()
    {
        var found = IlCallSiteScanner.Find(typeof(World.World).Assembly, IsCellMutator)
            .Select(site => (Type: site.Caller.FullName!, site.Method))
            .Distinct()
            .ToHashSet();

        var unlisted = found.Where(site => !CellWriters.ContainsKey(site)).Select(site => $"{site.Type}.{site.Method}").Order().ToArray();
        var stale = CellWriters.Keys.Where(key => !found.Contains(key)).Select(key => $"{key.Type}.{key.Method}").Order().ToArray();

        Assert.True(unlisted.Length == 0,
            "These methods write world cells but are not classified. Decide whether each is a policed player edit, an environment " +
            "writer (name its cause in plans/002-environment-change-policy.md) or world construction, then list it:\n" + string.Join("\n", unlisted));
        Assert.True(stale.Length == 0, "These entries no longer write a cell; remove them:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void Every_random_tick_behaviour_that_writes_names_its_cause()
    {
        var found = IlCallSiteScanner.Find(typeof(World.World).Assembly, IsRandomTickWrite)
            .Select(site => site.Caller.FullName!)
            .Where(type => type != typeof(RandomTickContext).FullName)
            .Distinct()
            .ToHashSet();

        var unlisted = found.Where(type => !RandomTickWriters.ContainsKey(type)).Order().ToArray();
        var stale = RandomTickWriters.Keys.Where(type => !found.Contains(type)).Order().ToArray();

        Assert.True(unlisted.Length == 0, "Random-tick behaviours that write a cell without a named cause:\n" + string.Join("\n", unlisted));
        Assert.True(stale.Length == 0, "Behaviours that no longer write a cell; remove them:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void The_classified_writers_that_are_not_yet_policed_are_exactly_the_environment_ones()
    {
        // Pins the size of the remaining gap: moving a writer from Environment to a policed kind is a deliberate edit of this test.
        var environment = CellWriters.Where(entry => entry.Value.Kind == Kind.Environment).Select(entry => entry.Key.Type.Split('.')[^1]).Distinct().Order();
        Assert.Equal(
            ["BlockUpdateSystem", "CreeperSystem", "FireSpread", "FluidFlow", "GravitySystem", "RandomTickContext"],
            environment);
    }
}
