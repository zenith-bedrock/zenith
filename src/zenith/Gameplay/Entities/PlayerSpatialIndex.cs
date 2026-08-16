using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXVIII — chunk-bucketed candidate index of every online player, rebuilt once per tick by
/// <see cref="PlayerSpatialIndexSystem"/> right after <see cref="Zenith.Gameplay.Survival.MovementSystem"/>
/// applies this tick's authoritative pose. Every online player is inserted regardless of
/// <c>IsInGame</c>/<c>IsDead</c> — those remain exact-state checks the caller performs on whatever
/// candidate it gets back, exactly as every consumer already did before this index existed. This
/// index only narrows "which players are spatially near," never "which players are valid targets."
/// </summary>
sealed class PlayerSpatialIndex
{
    private readonly ChunkSpatialIndex<Player.Player> _index = new();

    public void Rebuild(IReadOnlyList<Player.Player> online)
    {
        _index.Clear();
        foreach (var player in online)
            _index.Insert(player, player.PositionX, player.PositionZ);
    }

    public ChunkSpatialIndex<Player.Player>.NearbyEnumerable EnumerateNearby(float x, float z, float radius) =>
        _index.EnumerateNearby(x, z, radius);
}

/// <summary>
/// Rebuilds <see cref="PlayerSpatialIndex"/> from this tick's authoritative player positions.
/// Registered immediately after <c>MovementSystem</c> so every later system in the same tick
/// queries this tick's pose, not last tick's — the same ordering guarantee
/// <c>PlayerMeleeSystem</c>'s registration comment already documents for reach checks. Deliberately
/// its own tiny system rather than folded into <c>MovementSystem</c>: rebuilding a derived
/// acceleration structure is a distinct responsibility from applying input, and keeping it separate
/// means a future consumer that needs it can depend on registration order alone, not on reading
/// MovementSystem's implementation.
/// </summary>
sealed class PlayerSpatialIndexSystem : IGameSystem
{
    private readonly PlayerSpatialIndex _index;

    public PlayerSpatialIndexSystem(PlayerSpatialIndex index) => _index = index;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online) => _index.Rebuild(online);
}
