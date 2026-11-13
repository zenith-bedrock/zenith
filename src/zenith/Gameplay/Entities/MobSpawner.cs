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
    private readonly Dictionary<string, Func<int>> _liveCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(Action<float, float, float> Spawn, float X, float Y, float Z)> _pending = new();
    private bool _frozen;

    /// <summary>Names a species can be summoned by, in alphabetical order.</summary>
    public IReadOnlyList<string> Names => _species.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <param name="liveCount">How many of this species are alive right now; the natural spawner reads it to respect the mob cap.</param>
    public void Register(string name, Action<float, float, float> spawn, Func<int>? liveCount = null)
    {
        if (_frozen) throw new InvalidOperationException("Mob registration is closed after startup finalization.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(spawn);
        if (!_species.TryAdd(name, spawn))
            throw new InvalidOperationException($"A mob named '{name}' is already registered for summoning.");
        if (liveCount is not null) _liveCounts[name] = liveCount;
    }

    public bool IsRegistered(string name) => _species.ContainsKey(name);

    /// <summary>Validate natural-spawn references after species registration, then close registration before readers start.</summary>
    public void Freeze(SpawnTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        foreach (var entry in table.Entries)
        {
            if (!_species.ContainsKey(entry.Mob))
                throw new InvalidDataException($"{SpawnTable.ResourceName}: mob '{entry.Mob}' in category '{entry.Category.Name}' is not registered.");
            if (!_liveCounts.ContainsKey(entry.Mob))
                throw new InvalidDataException($"{SpawnTable.ResourceName}: mob '{entry.Mob}' in category '{entry.Category.Name}' has no population counter.");
        }
        _frozen = true;
    }

    /// <summary>Live count of a species, or 0 when it did not register a counter.</summary>
    public int LiveCount(string name) => _liveCounts.TryGetValue(name, out var count) ? count() : 0;

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
