using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Quais colunas já foram (ou estão a ser) enviadas a este jogador.
/// Usado por PreSpawn + ChunkStreamSystem — não é VisibilitySystem.
/// </summary>
sealed class PlayerChunkTracker
{
    private readonly object _gate = new();
    private readonly HashSet<(int X, int Z)> _known = new();
    private readonly Dictionary<(int X, int Z), int> _epoch = new();
    private readonly Queue<StreamCompletion> _completedStreams = new();
    private PreSpawnRequest? _pendingPreSpawn;
    private readonly List<(int X, int Z)> _scratch = new();
    private int _nextEpoch = 1;

    /// <summary>Raio de view em chunks (confirmado ao cliente).</summary>
    public int Radius { get; set; }

    /// <summary>
    /// Network receives the requested view radius, but only the gameplay owner starts the
    /// associated pre-spawn load and changes tracker state. One request/result is sufficient:
    /// session infrastructure rejects duplicate radius packets before this handoff.
    /// </summary>
    public bool TrySubmitPreSpawn(int viewRadius)
    {
        lock (_gate)
        {
            if (_pendingPreSpawn.HasValue)
                return false;
            _pendingPreSpawn = new PreSpawnRequest(viewRadius);
            return true;
        }
    }

    public bool TryConsumePreSpawnRequest(out PreSpawnRequest request)
    {
        lock (_gate)
        {
            if (_pendingPreSpawn is not { } pending)
            {
                request = default;
                return false;
            }

            _pendingPreSpawn = null;
            request = pending;
            return true;
        }
    }

    public int LastPublisherChunkX { get; private set; } = int.MinValue;
    public int LastPublisherChunkZ { get; private set; } = int.MinValue;

    /// <summary>
    /// Set on InGame enable — ChunkStreamSystem re-sends overlays for known columns once (§14).
    /// </summary>
    public bool NeedsOverlayResync { get; set; }

    /// <summary>True if this player already received (or began streaming) the column.</summary>
    public bool Knows(int chunkX, int chunkZ)
    {
        lock (_gate)
            return _known.Contains((chunkX, chunkZ));
    }

    /// <summary>Copy known column coords into <paramref name="dst"/> (cleared first).</summary>
    public void CopyKnown(List<(int X, int Z)> dst)
    {
        lock (_gate)
        {
            dst.Clear();
            foreach (var c in _known)
                dst.Add(c);
        }
    }

    /// <summary>Marca coluna como em voo/enviada. False se já conhecida.</summary>
    public bool TryBegin(int chunkX, int chunkZ) => TryBegin(chunkX, chunkZ, out _);

    /// <summary>
    /// Like <see cref="TryBegin(int, int)"/> but returns a stream <paramref name="epoch"/>.
    /// Stale async completions must call <see cref="IsStreamCurrent"/> before emit.
    /// </summary>
    public bool TryBegin(int chunkX, int chunkZ, out int epoch)
    {
        lock (_gate)
        {
            epoch = 0;
            var key = (chunkX, chunkZ);
            if (!_known.Add(key)) return false;
            epoch = _nextEpoch++;
            _epoch[key] = epoch;
            return true;
        }
    }

    /// <summary>True while this in-flight stream still owns the column slot.</summary>
    public bool IsStreamCurrent(int chunkX, int chunkZ, int epoch)
    {
        lock (_gate)
            return _epoch.TryGetValue((chunkX, chunkZ), out var e) && e == epoch;
    }

    public void Forget(int chunkX, int chunkZ)
    {
        lock (_gate)
        {
            var key = (chunkX, chunkZ);
            _known.Remove(key);
            _epoch.Remove(key);
        }
    }

    /// <summary>Drop known slot only if <paramref name="epoch"/> still matches (failed/stale stream).</summary>
    public bool TryAbandon(int chunkX, int chunkZ, int epoch)
    {
        lock (_gate)
        {
            var key = (chunkX, chunkZ);
            if (!_epoch.TryGetValue(key, out var e) || e != epoch) return false;
            _known.Remove(key);
            _epoch.Remove(key);
            return true;
        }
    }

    /// <summary>
    /// Accepts an asynchronous column-read result. The I/O continuation may only enqueue here;
    /// stream validity, player state and protocol emission stay owned by the GameLoop.
    /// </summary>
    public void CompleteStream(int chunkX, int chunkZ, int epoch, ColumnReadResult column)
    {
        lock (_gate)
            _completedStreams.Enqueue(StreamCompletion.Success(chunkX, chunkZ, epoch, column));
    }

    /// <summary>Queues a failed asynchronous read for tick-owned abandonment and logging.</summary>
    public void FailStream(int chunkX, int chunkZ, int epoch, string error)
    {
        lock (_gate)
            _completedStreams.Enqueue(StreamCompletion.Failure(chunkX, chunkZ, epoch, error));
    }

    /// <summary>Consumes one completed read from the GameLoop thread.</summary>
    public bool TryConsumeCompletedStream(out StreamCompletion completion)
    {
        lock (_gate)
        {
            if (_completedStreams.Count == 0)
            {
                completion = default;
                return false;
            }

            completion = _completedStreams.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Forget columns outside the view square. Uses a reused scratch list (no LINQ).
    /// Call when the publisher center chunk changes — not every tick.
    /// </summary>
    public void ForgetOutsideRadius(int centerX, int centerZ, int radius)
    {
        lock (_gate)
        {
            _scratch.Clear();
            foreach (var c in _known)
            {
                if (Math.Abs(c.X - centerX) > radius || Math.Abs(c.Z - centerZ) > radius)
                    _scratch.Add(c);
            }

            foreach (var c in _scratch)
            {
                _known.Remove(c);
                _epoch.Remove(c);
            }
        }
    }

    public void RememberMany(IEnumerable<(int X, int Z)> coords)
    {
        lock (_gate)
        {
            foreach (var c in coords)
            {
                if (!_known.Add(c)) continue;
                _epoch[c] = _nextEpoch++;
            }
        }
    }

    public bool PublisherCenterChanged(int chunkX, int chunkZ)
    {
        if (chunkX == LastPublisherChunkX && chunkZ == LastPublisherChunkZ)
            return false;
        LastPublisherChunkX = chunkX;
        LastPublisherChunkZ = chunkZ;
        return true;
    }

    /// <summary>
    /// Cross-reference audit finding, Phase XXIII-B polish pass — <see cref="PublisherCenterChanged"/>
    /// only compares chunk X/Z, never radius, so a mid-game render-distance increase (no chunk
    /// boundary crossed) never re-sent NetworkChunkPublisherUpdate. The client culls anything outside
    /// its last-told publish radius even after the server streams the new terrain, so newly streamed
    /// chunks at the edge of the increased view distance silently never render/tick. Resetting to the
    /// "never published" sentinel makes the next <see cref="PublisherCenterChanged"/> check report
    /// true unconditionally, forcing a fresh publish at the current center/radius.
    /// </summary>
    public void ForcePublisherRefresh()
    {
        LastPublisherChunkX = int.MinValue;
        LastPublisherChunkZ = int.MinValue;
    }

    public static int BlockToChunk(float block) => ChunkMath.BlockToChunk(block);

    public static void ForEachInSquare(int centerX, int centerZ, int radius, Action<int, int> visit)
    {
        for (var x = centerX - radius; x <= centerX + radius; x++)
        for (var z = centerZ - radius; z <= centerZ + radius; z++)
            visit(x, z);
    }

    /// <summary>Result of a background column read, awaiting tick-owned validation and emission.</summary>
    public readonly record struct StreamCompletion(
        int ChunkX,
        int ChunkZ,
        int Epoch,
        ColumnReadResult Column,
        string? Error)
    {
        public bool Succeeded => Error is null;

        public static StreamCompletion Success(int chunkX, int chunkZ, int epoch, ColumnReadResult column) =>
            new(chunkX, chunkZ, epoch, column, null);

        public static StreamCompletion Failure(int chunkX, int chunkZ, int epoch, string error) =>
            new(chunkX, chunkZ, epoch, default, error);
    }

    /// <summary>Immutable request passed from the network/session boundary to the tick.</summary>
    public readonly record struct PreSpawnRequest(int ViewRadius);

    /// <summary>
    /// Immutable snapshot captured by the gameplay owner before storage I/O starts. The worker
    /// returns it unchanged with columns; it never reads Player state after <c>await</c>.
    /// </summary>
    public readonly record struct PreSpawnSnapshot(
        int ViewRadius,
        int ReadyRadius,
        int CenterChunkX,
        int CenterChunkZ,
        int BlockX,
        int BlockY,
        int BlockZ);

}
