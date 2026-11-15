using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// A landed hit pushes any species whose definition has a <c>knockback</c> profile, away from the attacker, through the one
/// <see cref="MobCore.ApplyKnockback"/> and <see cref="KnockbackSystem"/>; resistance and absent profiles stop it.
/// </summary>
public sealed class KnockbackTests
{
    private static float Distance(EntityRuntime stores, EntityId id, float fromX, float fromZ)
    {
        Assert.True(stores.Positions.TryGet(id, out var pos));
        return MathF.Sqrt((pos.X - fromX) * (pos.X - fromX) + (pos.Z - fromZ) * (pos.Z - fromZ));
    }

    [Fact]
    public void A_cow_hit_by_a_player_is_pushed_away_and_the_push_fades()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var stores = new EntityRuntime();
        var cows = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var knockback = new KnockbackSystem(fx.World, stores);
        var id = cows.SpawnCow(attacker.PositionX + 1f, attacker.PositionY, attacker.PositionZ);
        var before = Distance(stores, id, attacker.PositionX, attacker.PositionZ);

        Assert.True(cows.TryApplyDamage(id, DamageSource.MeleeFrom(attacker.RuntimeId), 1f, fx.Players.Online, fx.Clock.CurrentTick));
        Assert.True(stores.Knockbacks.TryGet(id, out var push));
        Assert.True(push.X > 0f); // away from the attacker, who stands at -X of the cow

        var previous = before;
        for (var i = 0; i < 12; i++)
        {
            knockback.Tick(fx.Clock, fx.Players.Online);
            var now = Distance(stores, id, attacker.PositionX, attacker.PositionZ);
            Assert.True(now >= previous);
            previous = now;
        }

        Assert.True(previous > before + 0.3f);
        Assert.Equal(0f, stores.Knockbacks.GetRef(id).X); // faded to a stop
    }

    [Fact]
    public void A_golem_is_immune_by_its_data()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var stores = new EntityRuntime();
        var golems = new GolemSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var id = golems.SpawnGolem(attacker.PositionX + 1f, attacker.PositionY, attacker.PositionZ);

        golems.TryApplyDamage(id, DamageSource.MeleeFrom(attacker.RuntimeId), 1f, fx.Players.Online, fx.Clock.CurrentTick);

        Assert.Equal(0f, stores.Knockbacks.GetRef(id).X);
    }

    [Fact]
    public void A_wall_ends_the_push()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var stores = new EntityRuntime();
        var knockback = new KnockbackSystem(fx.World, stores);
        var cows = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var id = cows.SpawnCow(attacker.PositionX + 1f, attacker.PositionY, attacker.PositionZ);
        Assert.True(stores.Positions.TryGet(id, out var cow));
        var wallX = (int)MathF.Floor(cow.X) + 1;
        fx.World.SetBlock(wallX, (int)cow.Y, (int)MathF.Floor(cow.Z), Blocks.Stone);
        fx.World.SetBlock(wallX, (int)cow.Y + 1, (int)MathF.Floor(cow.Z), Blocks.Stone);
        stores.Knockbacks.GetRef(id).X = 1.5f; // one step lands inside the wall
        stores.Knockbacks.GetRef(id).DecayPerTick = 0.5f;

        knockback.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0f, stores.Knockbacks.GetRef(id).X);
    }

    [Fact]
    public void Only_species_with_a_knockback_profile_carry_the_component()
    {
        var fx = new IntentTestFixture();
        var stores = new EntityRuntime();
        var cow = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette).SpawnCow(0, -60, 0);
        var fish = new FishSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette).SpawnFish(0, -60, 0);
        var minecart = new MinecartSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette).SpawnMinecart(0, -60, 0);

        Assert.True(stores.Knockbacks.Has(cow));
        Assert.False(stores.Knockbacks.Has(fish)); // owns its swimming
        Assert.False(stores.Knockbacks.Has(minecart)); // owns its rolling
    }

    [Theory]
    [InlineData("""{"mobs":[{"name":"Zombie","maxHealth":5,"lootTable":"x","killExperience":1,"knockback":{"strength":0,"decayPerTick":0.5,"resistance":0}}]}""")]
    [InlineData("""{"mobs":[{"name":"Zombie","maxHealth":5,"lootTable":"x","killExperience":1,"knockback":{"strength":0.3,"decayPerTick":1,"resistance":0}}]}""")]
    [InlineData("""{"mobs":[{"name":"Zombie","maxHealth":5,"lootTable":"x","killExperience":1,"knockback":{"strength":0.3,"decayPerTick":0.5,"resistance":1.5}}]}""")]
    public void A_bad_knockback_profile_fails_to_load(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() => MobDefinitions.Parse(stream));
    }
}
