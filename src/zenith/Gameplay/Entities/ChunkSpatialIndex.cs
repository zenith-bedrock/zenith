using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXVIII — the smallest chunk-based spatial candidate index. Buckets arbitrary items by
/// the 16x16-block chunk (<see cref="ChunkMath"/> — the same floor-correct block-to-chunk
/// conversion <see cref="Zenith.Player.PlayerChunkTracker"/>/<see cref="World.World"/> already use,
/// not a second subtly-different implementation) containing a caller-supplied X/Z position.
///
/// This is a derived acceleration structure, never authoritative position storage: it is rebuilt
/// from whichever store already owns X/Z truth (ECS <c>Position</c>, a legacy actor's own fields,
/// <c>Player.PositionX/Z</c>) once per tick (see each consumer's own rebuild call site for exactly
/// when), not maintained incrementally. See docs/history/phases/phase-xxviii-spatial-query-scaling-findings.md,
/// "Rebuild vs incremental," for why: Zenith's ECS exposes direct mutable <c>ref</c> component
/// access from many independent systems, so an incremental index would need permanent, easy-to-miss
/// synchronization at every position-mutation site. A tick-scoped rebuild instead derives once from
/// whatever is authoritative at that moment and stays read-only for the rest of that phase.
///
/// The index answers only "which candidates occupy these nearby chunks?" — every exact
/// distance/height/state/eligibility rule stays with the caller. It never decides attack
/// eligibility, hit validity, targeting, despawn, or visibility.
/// </summary>
sealed class ChunkSpatialIndex<T>
{
    private readonly Dictionary<(int X, int Z), List<T>> _buckets = new();

    /// <summary>Empties every bucket but keeps the dictionary and each bucket's backing List&lt;T&gt; capacity, so a steady-state rebuild allocates nothing once buckets have grown to fit a tick's population.</summary>
    public void Clear()
    {
        foreach (var bucket in _buckets.Values)
            bucket.Clear();
    }

    public void Insert(T item, float x, float z)
    {
        var key = (ChunkMath.BlockToChunk(x), ChunkMath.BlockToChunk(z));
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = new List<T>();
            _buckets[key] = bucket;
        }

        bucket.Add(item);
    }

    /// <summary>
    /// Enumerates every candidate in a chunk occupying, or adjacent enough to overlap, the square
    /// <paramref name="radius"/> blocks around (<paramref name="x"/>, <paramref name="z"/>) — never
    /// only the single chunk containing the center point, so a query near a chunk edge still finds
    /// a candidate one chunk over. Allocation-free: a struct enumerator over the (small) chunk range
    /// intersecting the query, then each chunk's already-populated bucket list.
    /// </summary>
    public NearbyEnumerable EnumerateNearby(float x, float z, float radius) => new(_buckets, x, z, radius);

    public readonly struct NearbyEnumerable
    {
        private readonly Dictionary<(int X, int Z), List<T>> _buckets;
        private readonly int _minChunkX, _maxChunkX, _minChunkZ, _maxChunkZ;

        internal NearbyEnumerable(Dictionary<(int X, int Z), List<T>> buckets, float x, float z, float radius)
        {
            _buckets = buckets;
            _minChunkX = ChunkMath.BlockToChunk(x - radius);
            _maxChunkX = ChunkMath.BlockToChunk(x + radius);
            _minChunkZ = ChunkMath.BlockToChunk(z - radius);
            _maxChunkZ = ChunkMath.BlockToChunk(z + radius);
        }

        public Enumerator GetEnumerator() => new(_buckets, _minChunkX, _maxChunkX, _minChunkZ, _maxChunkZ);

        public struct Enumerator
        {
            private readonly Dictionary<(int X, int Z), List<T>> _buckets;
            private readonly int _maxChunkX, _minChunkZ, _maxChunkZ;
            private int _chunkX, _chunkZ;
            private List<T>? _currentBucket;
            private int _index;

            internal Enumerator(Dictionary<(int X, int Z), List<T>> buckets, int minChunkX, int maxChunkX, int minChunkZ, int maxChunkZ)
            {
                _buckets = buckets;
                _maxChunkX = maxChunkX;
                _minChunkZ = minChunkZ;
                _maxChunkZ = maxChunkZ;
                _chunkX = minChunkX;
                _chunkZ = minChunkZ - 1; // positioned one cell before the first, advanced by the first MoveNext
                _currentBucket = null;
                _index = -1;
            }

            public T Current => _currentBucket![_index];

            public bool MoveNext()
            {
                while (true)
                {
                    if (_currentBucket is not null && ++_index < _currentBucket.Count)
                        return true;

                    if (!AdvanceCell()) return false;
                    _buckets.TryGetValue((_chunkX, _chunkZ), out _currentBucket);
                    _index = -1;
                }
            }

            private bool AdvanceCell()
            {
                _chunkZ++;
                if (_chunkZ > _maxChunkZ)
                {
                    _chunkZ = _minChunkZ;
                    _chunkX++;
                }

                return _chunkX <= _maxChunkX;
            }
        }
    }
}
