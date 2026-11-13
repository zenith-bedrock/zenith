using Zenith.Gameplay.Entities;

namespace Zenith.Server.Composition;

/// <summary>
/// Every mob species, projectiles, and the two ways mobs enter the world (<c>/summon</c> and natural spawning).
///
/// DamageDispatch is one shared mutable instance, threaded through every species and consulted at Tick time, not snapshotted at
/// construction, so <c>Register</c> calls only need to land before the loop starts ticking. SkeletonSystem is the one real
/// construction-order dependency: it takes the already-built ProjectileSystem (it fires arrows through it). The spawner is created by
/// the composition root (<see cref="ServerServices.Spawner"/>) so the command layer can be built from it without depending on this
/// module having run.
/// </summary>
sealed class EntityModule : IGameplayModule
{
    public string Name => "entities";

    public void Register(ServerServices s)
    {
        var (world, players, entities, palette) = (s.World, s.Players, s.Entities, s.ItemPalette);
        var damage = new DamageDispatch();

        var zombie = new ZombieSystem(world, players, entities, palette);
        var minecart = new MinecartSystem(world, players, entities, palette);
        var cow = new CowSystem(world, players, entities, palette);
        var spider = new SpiderSystem(world, players, entities, palette);
        var fish = new FishSystem(world, players, entities, palette);
        var creeper = new CreeperSystem(world, players, entities, palette);
        var enderman = new EndermanSystem(world, players, entities, palette);
        var golem = new GolemSystem(world, players, entities, palette);
        var projectile = new ProjectileSystem(world, players, entities, damage, s.PlayerSpatial);
        var skeleton = new SkeletonSystem(world, players, entities, projectile, palette);
        var bat = new BatSystem(world, players, entities, palette);
        var villager = new VillagerSystem(world, players, entities, palette);

        damage.Register(zombie.Owns, zombie.TryApplyDamage);
        damage.Register(minecart.Owns, minecart.TryApplyDamage);
        damage.Register(cow.Owns, cow.TryApplyDamage);
        damage.Register(spider.Owns, spider.TryApplyDamage);
        damage.Register(fish.Owns, fish.TryApplyDamage);
        damage.Register(creeper.Owns, creeper.TryApplyDamage);
        damage.Register(enderman.Owns, enderman.TryApplyDamage);
        damage.Register(golem.Owns, golem.TryApplyDamage);
        damage.Register(skeleton.Owns, skeleton.TryApplyDamage);
        damage.Register(bat.Owns, bat.TryApplyDamage);
        damage.Register(villager.Owns, villager.TryApplyDamage);

        s.Register(zombie, "zombie");
        s.Register(minecart, "minecart");
        s.Register(projectile, "projectile");
        s.Register(skeleton, "skeleton");
        s.Register(cow, "cow");
        s.Register(creeper, "creeper");
        s.Register(enderman, "enderman");
        s.Register(bat, "bat");
        s.Register(spider, "spider");
        s.Register(villager, "villager");
        s.Register(golem, "golem");
        s.Register(fish, "fish");

        var spawner = s.Spawner;
        spawner.Register("zombie", (x, y, z) => zombie.SpawnZombie(x, y, z), () => zombie.Zombies.Count);
        spawner.Register("cow", (x, y, z) => cow.SpawnCow(x, y, z), () => cow.Cows.Count);
        spawner.Register("creeper", (x, y, z) => creeper.SpawnCreeper(x, y, z), () => creeper.Creepers.Count);
        spawner.Register("spider", (x, y, z) => spider.SpawnSpider(x, y, z), () => spider.Spiders.Count);
        spawner.Register("skeleton", (x, y, z) => skeleton.SpawnSkeleton(x, y, z), () => skeleton.Skeletons.Count);
        spawner.Register("iron_golem", (x, y, z) => golem.SpawnGolem(x, y, z));
        spawner.Register("enderman", (x, y, z) => enderman.SpawnEnderman(x, y, z), () => enderman.Endermen.Count);
        spawner.Register("fish", (x, y, z) => fish.SpawnFish(x, y, z));
        spawner.Register("minecart", (x, y, z) => minecart.SpawnMinecart(x, y, z));
        spawner.Register("bat", (x, y, z) => bat.SpawnBat(x, y, z), () => bat.Bats.Count);
        spawner.Register("villager", (x, y, z) => villager.SpawnVillager(x, y, z), () => villager.Villagers.Count);
        s.Register(spawner, "summon");
        var spawnTable = SpawnTable.Default;
        spawner.Freeze(spawnTable);
        s.Register(new NaturalSpawnSystem(world, spawner, spawnTable), "natural-spawn");
    }
}
