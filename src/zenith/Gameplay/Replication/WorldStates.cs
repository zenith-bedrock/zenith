using Zenith.Gameplay.Ports;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// One world setting that is versioned, stored with the world and replicated to every client when it changes (game rules, difficulty,
/// weather). The setting itself only holds a value and bumps its version when it changes; this binding says how to send it, how to
/// store it and how to read it back, so <see cref="WorldStateSyncSystem"/> and boot loading serve every setting alike.
/// </summary>
/// <param name="Name">The module-state key of a setting that stores itself the default way, and the name tests and logs use.</param>
/// <param name="Version">The setting's change counter.</param>
/// <param name="Send">The clients' view of it (the packet for a change; also what a joiner needs when it is not part of the join sequence).</param>
/// <param name="Save">Hands the setting to the world's write-behind storage.</param>
/// <param name="Load">Restores the setting at boot (the argument is whatever storage holds, null for nothing).</param>
sealed record WorldStateBinding(string Name, Func<long> Version, Action<IWorldChannel> Send, Action<World.World> Save, Action<World.World> Load)
{
    /// <summary>A setting stored as the opaque bytes of a module record named <paramref name="name"/>.</summary>
    public static WorldStateBinding Stored(
        string name, Func<long> version, Action<IWorldChannel> send, Func<byte[]> encode, Action<byte[]?> decode) =>
        new(name, version, send,
            world => world.PersistModuleState(name, encode()),
            world => decode(world.LoadModuleState(name)));
}

/// <summary>The world settings of a server: adding one is one binding here (and its own class).</summary>
static class WorldStates
{
    public static IReadOnlyList<WorldStateBinding> For(World.World world) =>
    [
        // Game rules keep their own storage slot and codec (the BDS wire order and the stored bytes of existing worlds).
        new("gamerules", () => world.Rules.Version, channel => channel.SendGameRules(world.Rules.Snapshot),
            w => w.PersistGameRules(), w => w.LoadGameRules()),
        WorldStateBinding.Stored("difficulty", () => world.Difficulty.Version, channel => channel.SendDifficulty((int)world.Difficulty.Value),
            world.Difficulty.Encode, world.Difficulty.Decode),
        WorldStateBinding.Stored("weather", () => world.Weather.Version, channel => channel.SendWeather(world.Weather.Raining, world.Weather.Thunder),
            world.Weather.Encode, world.Weather.Decode),
    ];

    /// <summary>Restores every setting from storage. Boot only.</summary>
    public static void LoadAll(World.World world)
    {
        foreach (var state in For(world)) state.Load(world);
    }

    public static void Save(World.World world, string name) =>
        For(world).Single(state => state.Name == name).Save(world);
}
