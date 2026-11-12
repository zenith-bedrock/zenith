using System.Collections.Concurrent;
using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// The one way to put a mob into the world on demand (the <c>/summon</c> command). Each species system registers its spawn
/// method under a name at composition; a request is queued (the command may run off the game thread) and carried out
/// inside the tick, so spawning never races the species system that owns the mob's stores.
/// </summary>
sealed class MobSpawner : IGameSystem
{
    private readonly Dictionary<string, Action<float, float, float>> _species = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(Action<float, float, float> Spawn, float X, float Y, float Z)> _pending = new();

    /// <summary>Names a species can be summoned by, in alphabetical order.</summary>
    public IReadOnlyList<string> Names => _species.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(string name, Action<float, float, float> spawn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_species.TryAdd(name, spawn))
            throw new InvalidOperationException($"A mob named '{name}' is already registered for summoning.");
    }

    /// <summary>Queues a spawn of <paramref name="name"/>; false when no such species is registered.</summary>
    public bool Request(string name, float x, float y, float z)
    {
        if (!_species.TryGetValue(name, out var spawn)) return false;
        _pending.Enqueue((spawn, x, y, z));
        return true;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        while (_pending.TryDequeue(out var request))
            request.Spawn(request.X, request.Y, request.Z);
    }
}
