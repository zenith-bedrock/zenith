using Xunit;
using Zenith.Ecs;
using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 005 follow-up: @ target selectors that reach entities and effects, from the grammar to the loop.</summary>
public sealed class SelectorTests
{
    public SelectorTests() => Blocks.EnsureLoaded();

    private static TargetSelector Parse(string text)
    {
        Assert.True(TargetSelector.TryParse(text, out var selector, out var error), error);
        return selector;
    }

    // ---- grammar --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_selector_carries_its_letter_and_its_arguments()
    {
        var selector = Parse("@e[type=minecraft:zombie,r=20,rm=2,c=-3,x=1,y=2,z=3,dx=4]");

        Assert.Equal(SelectorKind.AllEntities, selector.Kind);
        Assert.Equal("zombie", Assert.Single(selector.Types).Value);
        Assert.Equal(20f, selector.MaxDistance);
        Assert.Equal(2f, selector.MinDistance);
        Assert.Equal(-3, selector.Count);
        Assert.Equal((1f, 2f, 3f), (selector.X, selector.Y, selector.Z));
        Assert.True(selector.HasVolume);
    }

    [Theory]
    [InlineData("@x")]
    [InlineData("@e[type]")]
    [InlineData("@e[colour=red]")]
    [InlineData("@e[r=-1]")]
    [InlineData("@e[r=2,rm=5]")]
    [InlineData("@e[c=0]")]
    [InlineData("@e[m=adventure]")]
    [InlineData("@e type=zombie")]
    public void A_selector_that_cannot_work_is_refused_not_widened(string text)
    {
        Assert.False(TargetSelector.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void A_plain_word_is_a_player_name_and_a_quoted_name_may_hold_a_comma()
    {
        Assert.Equal(SelectorKind.Named, Parse("Steve").Kind);
        Assert.Equal("a,b", Assert.Single(Parse("@e[name=\"a,b\"]").Names).Value);
        var negated = Assert.Single(Parse("@e[type=!zombie]").Types);
        Assert.True(negated.Negated);
    }

    [Fact]
    public void Brackets_keep_a_selector_together_when_the_line_is_split()
    {
        Assert.Equal(
            ["effect", "@e[type=zombie, r=5]", "speed", "10"],
            CommandCatalog.Tokenize("/effect @e[type=zombie, r=5] speed 10"));
        Assert.Equal(["say", "a", "[b", "c"], CommandCatalog.Tokenize("say a [b c")); // only a selector token is bracket-aware
    }

    // ---- resolving ------------------------------------------------------------------------------------------------

    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly SelectorOrigin Origin = new(0, 0, 0, 1, CallerId);

    private static SelectorCandidate Mob(string type, float x, float y = 0, float z = 0) => new(type, type, x, y, z);

    private static SelectorCandidate Person(string name, float x, long session, Guid? id = null, int level = 0, GameMode mode = GameMode.Survival) =>
        new(SelectorCandidate.PlayerType, name, x, 0, 0, session, id ?? Guid.NewGuid(), Level: level, Mode: mode);

    private static IReadOnlyList<SelectorCandidate> World() =>
    [
        Person("alex", 0, 1, CallerId, level: 10, mode: GameMode.Creative),
        Person("bob", 5, 2, level: 2),
        Mob("zombie", 3), Mob("zombie", 12), Mob("cow", 4)
    ];

    private static List<string> Names(string selector, SelectorOrigin? origin = null, int seed = 1) =>
        SelectorResolver.Resolve(Parse(selector), origin ?? Origin, World(), new Random(seed)).Select(c => c.Name).ToList();

    [Fact]
    public void Each_letter_starts_from_its_own_pool()
    {
        Assert.Equal(["alex"], Names("@s"));
        Assert.Equal(["alex", "bob"], Names("@a"));
        Assert.Equal(["alex"], Names("@p"));
        Assert.Equal(["bob"], Names("@p", Origin with { X = 6 }));
        Assert.Equal(5, Names("@e").Count);
        Assert.Equal(["alex", "bob"], Names("@e[type=player]").OrderBy(n => n).ToList());
    }

    [Fact]
    public void Type_filters_and_negates_and_the_count_takes_the_nearest_or_the_farthest()
    {
        Assert.Equal(["zombie", "zombie"], Names("@e[type=zombie]"));
        Assert.Equal(3, Names("@e[type=!zombie]").Count);
        var near = SelectorResolver.Resolve(Parse("@e[type=zombie,c=1]"), Origin, World(), new Random(1));
        Assert.Equal(3f, Assert.Single(near).X);
        var far = SelectorResolver.Resolve(Parse("@e[type=zombie,c=-1]"), Origin, World(), new Random(1));
        Assert.Equal(12f, Assert.Single(far).X);
    }

    [Fact]
    public void Distance_origin_and_volume_narrow_the_pick()
    {
        Assert.Single(Names("@e[type=zombie,r=5]"));
        Assert.Single(Names("@e[type=zombie,rm=5]"));
        Assert.Single(Names("@e[type=zombie,x=10,r=3]"));
        Assert.Equal(2, Names("@e[type=zombie,dx=20]").Count);
        Assert.Single(Names("@e[type=zombie,dx=3]")); // the box is [0, 3 + 1]
        Assert.Empty(Names("@e[type=zombie,dx=-5]"));
    }

    [Fact]
    public void A_tilde_offsets_the_origin_from_where_the_command_ran()
    {
        // Zombies at x=3 and x=12; from the origin 10, "~2" is 12 and "~" is 10.
        var at = Origin with { X = 10 };
        Assert.Equal(["zombie"], Names("@e[type=zombie,x=~2,r=1]", at));
        Assert.Empty(Names("@e[type=zombie,x=~,r=1]", at));
        Assert.Equal(["zombie"], Names("@e[type=zombie,x=~-7,r=1]", at)); // 10 - 7 = 3
        Assert.Equal(["zombie"], Names("@e[type=zombie,x=2,r=1]", at));   // absolute: unaffected by the origin
        Assert.False(TargetSelector.TryParse("@e[x=~abc]", out _, out _));
    }

    [Fact]
    public void Game_mode_and_level_apply_to_players_only()
    {
        Assert.Equal(["alex"], Names("@e[m=creative]"));
        Assert.Equal(["bob"], Names("@a[m=!creative]"));
        Assert.Equal(["alex"], Names("@e[l=20,lm=5]"));
        Assert.Empty(Names("@e[type=zombie,m=survival]"));
    }

    [Fact]
    public void A_random_pick_is_one_player_unless_a_type_widens_it()
    {
        Assert.Contains(Assert.Single(Names("@r")), new[] { "alex", "bob" });
        Assert.Equal(2, Names("@r[c=2]").Count);
        Assert.Equal("zombie", Assert.Single(Names("@r[type=zombie]")));
    }

    [Fact]
    public void A_name_finds_the_player_whatever_the_case_and_an_unknown_one_finds_nobody()
    {
        Assert.Equal(["bob"], Names("BOB"));
        Assert.Empty(Names("nobody"));
        Assert.Equal(["bob"], Names("@e[name=bob]"));
    }

    // ---- commands, end to end -------------------------------------------------------------------------------------

    private sealed class Rig
    {
        public required IntentTestFixture Fx { get; init; }
        public required EntityRuntime Stores { get; init; }
        public required EntityId Zombie { get; init; }
        public required CommandRuntime Commands { get; init; }
        public required EntityCommandSystem System { get; init; }
        public required Player.Player Op { get; init; }
        public required RecordingClientChannel Client { get; init; }

        public void Run(string line, Player.Player? who = null) { Commands.Execute(who ?? Op, line); System.Tick(Fx.Clock, Fx.Players.Online); }
        public string? Toast => Client.Of("SendToast").Select(a => (string?)a[1]).LastOrDefault();
    }

    private static async Task<Rig> Build()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, 199, z, Blocks.Stone);
                for (var y = 200; y < 212; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        var stores = new EntityRuntime();
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(zombies);
        damage.Mitigate = (id, source, amount) => MobMitigation.Apply(stores, id, source, amount);
        var zombie = zombies.SpawnZombie(8.5f, 200f, 8.5f);

        var directory = new MobDirectory();
        directory.Register("zombie", zombies.Owns);
        var spawner = new MobSpawner(fx.Context.Gate);
        var op = fx.AddOperator("op");
        var client = new RecordingClientChannel();
        op.AttachClient(client);
        var system = new EntityCommandSystem(
            stores, fx.Players, damage, new MobEffectSystem(stores, damage), directory, spawner.Targeted, fx.Context.Gate);
        var commands = new CommandRuntime(fx.Players, fx.Context.Permissions, spawner: spawner);
        return new Rig { Fx = fx, Stores = stores, Zombie = zombie, Commands = commands, System = system, Op = op, Client = client };
    }

    [Fact]
    public async Task An_effect_given_to_a_selector_reaches_the_mobs_it_picks()
    {
        var rig = await Build();

        var feedback = rig.Commands.Execute(rig.Op, "/effect @e[type=zombie] poison 10 1");
        Assert.Equal(CommandOutcome.Accepted, feedback.Outcome);
        Assert.False(rig.Stores.TryGetBody(rig.Zombie, out var before) && before.Effects.Active.Count > 0); // queued, not applied yet

        rig.System.Tick(rig.Fx.Clock, rig.Fx.Players.Online);

        Assert.True(rig.Stores.TryGetBody(rig.Zombie, out var body));
        var effect = Assert.Single(body.Effects.Active).Value;
        Assert.Equal((EffectType.Poison, 1), (effect.Type, effect.Amplifier));
        Assert.Contains("1 target", rig.Toast);
    }

    [Fact]
    public async Task A_selector_that_matches_nothing_says_so_and_changes_nothing()
    {
        var rig = await Build();

        rig.Run("/effect @e[type=cow] poison");

        Assert.Equal("No targets matched.", rig.Toast);
        Assert.True(rig.Stores.TryGetBody(rig.Zombie, out var body));
        Assert.Empty(body.Effects.Active);
    }

    [Fact]
    public async Task Clearing_takes_every_effect_off_the_selected_mobs()
    {
        var rig = await Build();
        rig.Run("/effect @e[type=zombie] poison 30");
        rig.Stores.TryGetBody(rig.Zombie, out var body);
        Assert.NotEmpty(body.Effects.Active);

        rig.Run("/effect @e[type=zombie] clear");

        Assert.Empty(body.Effects.Active);
    }

    [Fact]
    public async Task Clearing_one_named_effect_leaves_the_others()
    {
        var rig = await Build();
        rig.Run("/effect @e[type=zombie] poison 30");
        rig.Run("/effect @e[type=zombie] weakness 30");
        rig.Stores.TryGetBody(rig.Zombie, out var body);
        Assert.Equal(2, body.Effects.Active.Count);

        rig.Run("/effect @e[type=zombie] clear poison");

        Assert.Equal(EffectType.Weakness, Assert.Single(body.Effects.Active).Key);

        rig.Run("/effect @s clear poison"); // the caller has no poison: nothing is queued
        Assert.False(rig.Op.Effects.TryConsumeEffectIntent(out _));
    }

    [Fact]
    public async Task Kill_ends_the_selected_mobs_through_their_own_damage_path()
    {
        var rig = await Build();

        rig.Run("/kill @e[type=zombie]");

        Assert.False(rig.Stores.Health.TryGet(rig.Zombie, out var health) && !health.State.IsDead);
    }

    [Fact]
    public async Task Kill_with_no_target_is_the_caller_and_ends_them()
    {
        var rig = await Build();

        rig.Run("/kill");

        Assert.True(rig.Op.IsDead);
    }

    [Fact]
    public async Task The_selector_form_of_effect_works_on_players_by_name_and_by_at_s()
    {
        var rig = await Build();

        rig.Run("/effect @s regen 5");

        Assert.True(rig.Op.Effects.TryConsumeEffectIntent(out var intent));
        Assert.Equal((false, EffectType.Regeneration, 100), (intent.ClearAll, intent.Type, intent.DurationTicks));
    }

    [Fact]
    public async Task Game_mode_follows_the_selector_to_every_player_it_picks()
    {
        var rig = await Build();
        var other = rig.Fx.AddInGamePlayer("other");
        other.AttachClient(new RecordingClientChannel());

        rig.Run("/gamemode creative @a");

        Assert.True(rig.Op.Intents.TryConsumeGameMode(out var mine));
        Assert.True(other.Intents.TryConsumeGameMode(out var theirs));
        Assert.Equal((GameMode.Creative, GameMode.Creative), (mine.Mode, theirs.Mode));
    }

    [Fact]
    public async Task A_player_not_online_is_refused_at_once_and_a_bad_selector_is_explained()
    {
        var rig = await Build();

        Assert.Equal(CommandOutcome.Rejected, rig.Commands.Execute(rig.Op, "/effect ghost poison").Outcome);
        var bad = rig.Commands.Execute(rig.Op, "/effect @q poison");
        Assert.Equal(CommandOutcome.Rejected, bad.Outcome);
        Assert.Contains("Unknown selector", bad.Message);
        Assert.Contains("not key=value", rig.Commands.Execute(rig.Op, "/kill @e[type]").Message);
    }

    [Fact]
    public async Task Someone_without_the_permission_cannot_use_a_selector_to_reach_others()
    {
        var rig = await Build();
        var pleb = rig.Fx.AddInGamePlayer("pleb");
        pleb.AttachClient(new RecordingClientChannel());

        Assert.Equal(CommandOutcome.Rejected, rig.Commands.Execute(pleb, "/effect @e[type=zombie] poison").Outcome);
        Assert.Equal(CommandOutcome.Rejected, rig.Commands.Execute(pleb, "/kill @e").Outcome);
        rig.System.Tick(rig.Fx.Clock, rig.Fx.Players.Online);

        Assert.True(rig.Stores.TryGetBody(rig.Zombie, out var body));
        Assert.Empty(body.Effects.Active);
        Assert.True(rig.Stores.Health.TryGet(rig.Zombie, out var health) && !health.State.IsDead);
    }

    [Fact]
    public async Task A_grant_revoked_while_queued_stops_the_effect()
    {
        var rig = await Build();
        rig.Commands.Execute(rig.Op, "/kill @e[type=zombie]");
        rig.Fx.Players.Remove(rig.Op); // the issuer left before the loop ran it

        rig.System.Tick(rig.Fx.Clock, rig.Fx.Players.Online);

        Assert.True(rig.Stores.Health.TryGet(rig.Zombie, out var health) && !health.State.IsDead);
    }

    [Fact]
    public async Task The_legacy_forms_still_work()
    {
        var rig = await Build();

        Assert.Equal(CommandOutcome.Accepted, rig.Commands.Execute(rig.Op, "/effect give poison 200 1").Outcome);
        Assert.Equal(CommandOutcome.Accepted, rig.Commands.Execute(rig.Op, "/effect clear").Outcome);
        Assert.Equal(CommandOutcome.Accepted, rig.Commands.Execute(rig.Op, "/gamemode creative op").Outcome);
    }
}
