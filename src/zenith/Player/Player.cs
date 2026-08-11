using Zenith.Gameplay;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Entidade lógica de um jogador conectado. Não sabe nada sobre raknet nem wire format.
/// Estado de gameplay (posição) só é mutado no GameLoop; a rede só escreve <see cref="MovementInputState"/>.
/// </summary>
class Player
{
    public const int MaxPendingBlockEdits = 8;
    public const int MaxPendingInventoryStacks = 8;
    public const int MaxPendingDig = 4;
    public const int MaxPendingWindowIntents = 4;
    public const int MaxPendingChat = 8;

    /// <summary>Vanilla player maximum for the current first health slice; not an attribute framework.</summary>
    public const float DefaultMaxHealth = 20f;

    /// <summary>Euclidean interact reach (blocks) from eye to target center — Fase 3 simple authority.</summary>
    public const float MaxBlockReach = 6f;

    private readonly object _movementInputLock = new();
    private readonly object _attackIntentLock = new();
    private bool _pendingAttackIntent;
    private MovementInputState _movementInput;
    private readonly object _blockEditLock = new();
    private readonly Queue<BlockEditIntent> _blockEdits = new();
    private readonly object _inventoryStackLock = new();
    private readonly Queue<InventoryStackIntent> _inventoryStacks = new();
    // GameLoop-owned replay ledger: request IDs are protocol correlation tokens, not item authority.
    private readonly HashSet<int> _claimedInventoryRequests = new();
    private readonly Queue<int> _claimedInventoryRequestOrder = new();
    private readonly object _digLock = new();
    private readonly Queue<DigIntent> _digIntents = new();
    private DigIntent? _provisionalDig;
    private DigIntent? _suppressedDigAuthorization;
    private readonly object _windowLock = new();
    private readonly Queue<InventoryWindowIntent> _windowIntents = new();
    private readonly object _chatLock = new();
    private readonly Queue<string> _pendingChat = new();
    private readonly object _gameModeLock = new();
    private GameMode? _pendingGameMode;
    private readonly object _respawnLock = new();
    private bool _pendingRespawn;
    private readonly HealthState _health = new(DefaultMaxHealth);
    private bool _deathTransitionFinalized;
    private readonly object _spawnReadyLock = new();
    private bool _pendingSpawnReady;
    // The GameLoop is the only writer. This lock is a narrow snapshot handoff for the network
    // decoder, which must bind a wire container action to the session it observed.
    private readonly object _containerLock = new();
    private OpenContainerSession? _openContainer;
    // Connection lifecycle gates are intentionally independent scalars written by the session
    // boundary and observed by the GameLoop. They are not gameplay authority, but visibility
    // matters when cancelling a just-disconnected player's pending input.
    private volatile bool _isInGame;
    private volatile bool _isSpawning;

    public string Username { get; }
    public NetworkSession Session { get; }

    /// <summary>Runtime entity id enviado no StartGame / MoveActorAbsolute.</summary>
    public long RuntimeId { get; }

    /// <summary>UUID de lista/AddPlayer.</summary>
    public Guid Uuid { get; }

    /// <summary>
    /// False when login minted an ephemeral Guid — skip <c>pd:</c> / prefer no reconnect honesty (ADR §60).
    /// </summary>
    public bool IdentityStable { get; }

    /// <summary>
    /// True after SetLocalPlayerAsInitialized → InGame. Connection-lifecycle scalar deliberately
    /// written by session transitions (ADR §97), not an intent: it gates visibility and protocol
    /// handling but does not itself mutate world, inventory, combat, or movement authority.
    /// </summary>
    public bool IsInGame
    {
        get => _isInGame;
        set => _isInGame = value;
    }

    /// <summary>
    /// True while SpawnResponse (after PLAYER_SPAWN, before InGame). Allows ChunkStream
    /// to fill the view ring during loading (ADR §70). Like <see cref="IsInGame"/>, this is a
    /// direct connection-lifecycle transition rather than gameplay state owned by a simulation.
    /// </summary>
    public bool IsSpawning
    {
        get => _isSpawning;
        set => _isSpawning = value;
    }

    /// <summary>
    /// Hotbar 0–8; selected slot bounds-checked no handler. Intentional rule-6 exception:
    /// handlers write this directly (not via a pending-intent queue) — atomic scalar,
    /// <see cref="Gameplay.Systems.EquipmentSystem"/> diffs + fans out on tick (ADR §80).
    /// </summary>
    public int SelectedHotbarSlot { get; set; }

    public PlayerInventory Inventory { get; }

    /// <summary>Join-time from config (§31); runtime via GameModeSystem only (§52). Never handlers.</summary>
    public GameMode GameMode { get; private set; }

    /// <summary>Colunas enviadas / em voo e raio de view (streaming).</summary>
    public PlayerChunkTracker Chunks { get; } = new();

    /// <summary>Held hotbar stack identity (ADR §55).</summary>
    public StackId HeldStackId => Inventory.GetStackId(SelectedHotbarSlot);

    /// <summary>
    /// Classic RGBA mirror of Session.Skin when dimensions match (PlayerList fallback /
    /// mid-game classic update). Full wire skin lives on <c>NetworkSession.Skin</c> (ADR §49).
    /// </summary>
    public byte[]? SkinRgba { get; set; }
    public uint SkinWidth { get; set; }
    public uint SkinHeight { get; set; }

    public float PositionX { get; set; }
    public float PositionY { get; set; } = Blocks.FlatSpawnY;
    public float PositionZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }

    /// <summary>Read-only projection of the GameLoop-owned health state.</summary>
    public float Health => _health.Current;

    /// <summary>Maximum represented by this player's composed health state.</summary>
    public float MaxHealth => _health.Maximum;

    /// <summary>
    /// Domain vitals (ADR §40). Hunger remains a frozen HUD value; health has an explicit
    /// authoritative mutation path through <see cref="ApplyDamage"/>.
    /// </summary>
    public float Hunger { get; set; } = 20f;

    /// <summary>Highest feet Y reached since last on-ground (ADR §96 fall damage). Reset on landing.</summary>
    public float FallPeakY { get; set; } = Blocks.FlatSpawnY;

    /// <summary>True while death screen is up — AuthInput/edits ignored until respawn tick (§40).</summary>
    public bool IsDead => _health.IsDead;

    /// <summary>Cause string last sent via DeathInfo (tests / Debug).</summary>
    public string DeathCause => _health.FatalSource?.DeathInfoCause ?? "";

    /// <summary>Último held replicado a peers (EquipmentSystem).</summary>
    public int LastReplicatedHotbarSlot { get; set; } = -1;
    public StackId LastReplicatedHeldStackId { get; set; }
    public int LastReplicatedHeldCount { get; set; } = int.MinValue;

    /// <summary>Última pose enviada a peers via MoveActorAbsolute (MovementSystem dirty-check).</summary>
    public float LastReplicatedX { get; set; }
    public float LastReplicatedY { get; set; }
    public float LastReplicatedZ { get; set; }
    public float LastReplicatedPitch { get; set; }
    public float LastReplicatedYaw { get; set; }
    public float LastReplicatedHeadYaw { get; set; }

    /// <summary>AuthInput pose modes applied on tick (§53).</summary>
    public bool IsSneaking { get; set; }
    public bool IsSprinting { get; set; }

    /// <summary>AuthInput VerticalCollision — peer Absolute ON_GROUND (default true until first input).</summary>
    public bool IsOnGround { get; set; } = true;

    public bool LastReplicatedSneaking { get; set; }
    public bool LastReplicatedSprinting { get; set; }
    public bool LastReplicatedOnGround { get; set; } = true;

    /// <summary>GameClock tick of last relayed emote (rate-limit §53).</summary>
    public ulong LastEmoteTick { get; set; }

    /// <summary>Server-authoritative break progress (AuthInput start → predict). Cleared on abort/success.</summary>
    private int _breakTargetX;
    private int _breakTargetY;
    private int _breakTargetZ;
    private ulong _breakStartedTick;
    private int _breakRequiredTicks;
    private StackId _digHeldStackId;
    private bool _hasBreakTarget;
    private ulong _lastDigActivityTick;

    public int BreakTargetX { get { lock (_digLock) return _breakTargetX; } }
    public int BreakTargetY { get { lock (_digLock) return _breakTargetY; } }
    public int BreakTargetZ { get { lock (_digLock) return _breakTargetZ; } }
    public ulong BreakStartedTick { get { lock (_digLock) return _breakStartedTick; } }
    /// <summary>Dig duration snapshotted at <see cref="BeginBreak"/> (GameLoop ticks).</summary>
    public int BreakRequiredTicks { get { lock (_digLock) return _breakRequiredTicks; } }
    /// <summary>Held <see cref="StackId"/> at dig start / last retarget (ADR §55).</summary>
    public StackId DigHeldStackId { get { lock (_digLock) return _digHeldStackId; } }
    public bool HasBreakTarget { get { lock (_digLock) return _hasBreakTarget; } }

    /// <summary>
    /// Last GameClock tick that saw start/crack/continue for the dig target.
    /// After <see cref="BreakRequiredTicks"/> + <see cref="DigIdleAbortTicks"/> without activity → StopCrack.
    /// </summary>
    public ulong LastDigActivityTick { get { lock (_digLock) return _lastDigActivityTick; } }

    /// <summary>
    /// Post-dig-window grace without dig AuthInput before aborting crack (~2s @ 20 TPS).
    /// Idle abort must not fire before <see cref="BreakRequiredTicks"/> elapse (§27).
    /// </summary>
    public const ulong DigIdleAbortTicks = 40;

    private uint _nextContainerGeneration;

    /// <summary>Single authoritative active container for this connection; assigned on GameLoop.</summary>
    public OpenContainerSession? OpenContainer
    {
        get
        {
            lock (_containerLock)
                return _openContainer;
        }
    }

    /// <summary>
    /// Takes a coherent protocol-to-domain container-session snapshot at the network handoff.
    /// It grants no mutation authority; the InventorySystem revalidates its generation on tick.
    /// </summary>
    public bool TryGetOpenContainerSession(out OpenContainerSession session)
    {
        lock (_containerLock)
        {
            if (_openContainer is not { } active)
            {
                session = default;
                return false;
            }

            session = active;
            return true;
        }
    }

    /// <summary>Compatibility projection of the active chest target; null for every other view.</summary>
    public OpenChestView? OpenChest => OpenContainer is { Target: OpenContainerSession.TargetKind.Chest, Chest: { } chest }
        ? chest
        : null;

    /// <summary>Compatibility projection of the active player-inventory window.</summary>
    public bool InventoryWindowOpen => OpenContainer is { Target: OpenContainerSession.TargetKind.PlayerInventory };

    public OpenContainerSession OpenPlayerContainer(byte windowId, byte windowType)
    {
        lock (_containerLock)
        {
            var session = OpenContainerSession.PlayerInventory(windowId, windowType, ++_nextContainerGeneration);
            _openContainer = session;
            return session;
        }
    }

    public OpenContainerSession OpenChestContainer(byte windowId, byte windowType, in OpenChestView view)
    {
        lock (_containerLock)
        {
            var session = OpenContainerSession.ChestView(windowId, windowType, ++_nextContainerGeneration, view);
            _openContainer = session;
            return session;
        }
    }

    public bool TryCloseContainer(byte windowId, byte windowType, out OpenContainerSession closed)
    {
        lock (_containerLock)
        {
            if (_openContainer is not { } active || active.WindowId != windowId || active.WindowType != windowType)
            {
                closed = default;
                return false;
            }

            _openContainer = null;
            closed = active;
            return true;
        }
    }

    public bool TryClearOpenContainer(out OpenContainerSession closed)
    {
        lock (_containerLock)
        {
            if (_openContainer is not { } active)
            {
                closed = default;
                return false;
            }

            _openContainer = null;
            closed = active;
            return true;
        }
    }

    /// <summary>Ephemeral 2×2 craft grid — not persisted.</summary>
    public PlayerCraftUi CraftUi { get; } = new();

    /// <summary>GameLoop-only authoritative start; network submits <see cref="DigIntent"/> instead.</summary>
    public void BeginBreak(int x, int y, int z, ulong tick, int requiredTicks, StackId heldStackId = default)
    {
        lock (_digLock)
        {
            _breakTargetX = x;
            _breakTargetY = y;
            _breakTargetZ = z;
            _breakStartedTick = tick;
            _breakRequiredTicks = requiredTicks;
            _digHeldStackId = heldStackId;
            _hasBreakTarget = true;
            _lastDigActivityTick = tick;
        }
    }

    /// <summary>Refresh dig activity (same-cell crack/continue) so idle abort does not fire.</summary>
    public void MarkDigActive(ulong tick)
    {
        lock (_digLock)
        {
            if (!_hasBreakTarget) return;
            _lastDigActivityTick = tick;
        }
    }

    /// <summary>Progress-preserving dig retarget when held tool changes mid-break (ADR §27).</summary>
    public void RetargetBreakTiming(ulong startedTick, int requiredTicks, StackId heldStackId)
    {
        lock (_digLock)
        {
            if (!_hasBreakTarget) return;
            _breakStartedTick = startedTick;
            _breakRequiredTicks = requiredTicks;
            _digHeldStackId = heldStackId;
        }
    }

    /// <summary>GameLoop-only authoritative abort; network submits an abort <see cref="DigIntent"/> instead.</summary>
    public void AbortBreak()
    {
        lock (_digLock)
        {
            _hasBreakTarget = false;
            _breakRequiredTicks = 0;
            _digHeldStackId = default;
            _lastDigActivityTick = 0;
            _provisionalDig = null;
        }
    }

    /// <summary>
    /// Network-side handoff only: suppresses authorization until the queued edit/abort is applied.
    /// It never mutates the tick-owned break target.
    /// </summary>
    public void CancelDigAuthorization(int x, int y, int z)
    {
        lock (_digLock)
        {
            _suppressedDigAuthorization = DigIntent.Abort(x, y, z);
            _provisionalDig = null;
            CancelPendingDigStartUnsafe(x, y, z);
        }
    }

    public bool IsBreakTarget(int x, int y, int z)
    {
        lock (_digLock)
            return _hasBreakTarget && _breakTargetX == x && _breakTargetY == y && _breakTargetZ == z;
    }

    /// <summary>Gets one coherent break-state snapshot for readers outside the GameLoop.</summary>
    public bool TryGetBreakState(out BreakState state)
    {
        lock (_digLock)
        {
            if (!_hasBreakTarget)
            {
                state = default;
                return false;
            }

            state = new BreakState(
                _breakTargetX, _breakTargetY, _breakTargetZ,
                _breakStartedTick, _breakRequiredTicks, _digHeldStackId, _lastDigActivityTick);
            return true;
        }
    }

    /// <summary>
    /// Dig auth for same-packet Predict before tick applies <see cref="BeginBreak"/> (§27/§54).
    /// </summary>
    public bool TryGetDigAuth(int x, int y, int z, out ulong startedTick, out int requiredTicks)
    {
        lock (_digLock)
        {
            if (_suppressedDigAuthorization is { } suppressed &&
                suppressed.X == x && suppressed.Y == y && suppressed.Z == z)
                goto NoAuth;
            if (_hasBreakTarget && _breakTargetX == x && _breakTargetY == y && _breakTargetZ == z)
            {
                startedTick = _breakStartedTick;
                requiredTicks = _breakRequiredTicks;
                return true;
            }

            if (_provisionalDig is { HasValue: true, IsAbort: false } dig &&
                dig.X == x && dig.Y == y && dig.Z == z)
            {
                startedTick = dig.StartedTick;
                requiredTicks = dig.RequiredTicks;
                return true;
            }
        }

    NoAuth:
        startedTick = 0;
        requiredTicks = 0;
        return false;
    }

    /// <summary>Queue dig start (handler). Provisional auth for same-packet Predict.</summary>
    public bool SubmitDigStart(
        int x, int y, int z, ulong startedTick, int requiredTicks, StackId heldStackId = default)
    {
        if (IsDead) return false;
        var intent = DigIntent.Start(x, y, z, startedTick, requiredTicks, heldStackId);
        lock (_digLock)
        {
            if (_digIntents.Count >= MaxPendingDig)
                return false;
            _digIntents.Enqueue(intent);
            _provisionalDig = intent;
            _suppressedDigAuthorization = null;
            return true;
        }
    }

    /// <summary>
    /// Queue dig abort (handler). Suppresses same-packet auth; tick owns the actual abort.
    /// </summary>
    public bool SubmitDigAbort(int x, int y, int z)
    {
        var intent = DigIntent.Abort(x, y, z);
        lock (_digLock)
        {
            if (_digIntents.Count >= MaxPendingDig)
                return false;
            _digIntents.Enqueue(intent);
            _provisionalDig = null;
            _suppressedDigAuthorization = intent;
            return true;
        }
    }

    /// <summary>Queues refresh activity; only BlockDigSystem updates the active dig timestamp.</summary>
    public bool SubmitDigActivity(int x, int y, int z, ulong tick)
    {
        lock (_digLock)
        {
            if (_digIntents.Count >= MaxPendingDig) return false;
            _digIntents.Enqueue(DigIntent.Activity(x, y, z, tick));
            return true;
        }
    }

    public bool SubmitDigActivityForActive(ulong tick)
    {
        lock (_digLock)
        {
            if (!_hasBreakTarget || _digIntents.Count >= MaxPendingDig) return false;
            _digIntents.Enqueue(DigIntent.Activity(_breakTargetX, _breakTargetY, _breakTargetZ, tick));
            return true;
        }
    }

    public bool TryConsumeDig(out DigIntent intent)
    {
        lock (_digLock)
        {
            if (_digIntents.Count == 0)
            {
                intent = default;
                return false;
            }

            intent = _digIntents.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Drop a queued Start for the cell after DigAuthorized break is queued (same-packet Start+Predict).
    /// </summary>
    public void CancelPendingDigStart(int x, int y, int z)
    {
        lock (_digLock)
        {
            _provisionalDig = null;
            CancelPendingDigStartUnsafe(x, y, z);
        }
    }

    private void CancelPendingDigStartUnsafe(int x, int y, int z)
    {
            if (_digIntents.Count == 0) return;
            var kept = new Queue<DigIntent>(_digIntents.Count);
            var removed = false;
            while (_digIntents.Count > 0)
            {
                var d = _digIntents.Dequeue();
                if (!removed && !d.IsAbort && d.X == x && d.Y == y && d.Z == z)
                {
                    removed = true;
                    continue;
                }

                kept.Enqueue(d);
            }

            while (kept.Count > 0)
                _digIntents.Enqueue(kept.Dequeue());
    }

    /// <summary>Coherent view of one active break target and its server-authoritative timing.</summary>
    public readonly record struct BreakState(
        int X,
        int Y,
        int Z,
        ulong StartedTick,
        int RequiredTicks,
        StackId HeldStackId,
        ulong LastActivityTick);

    public bool SubmitWindowIntent(in InventoryWindowIntent intent)
    {
        if (IsDead && intent.Action != InventoryWindowIntent.Kind.Close) return false;
        lock (_windowLock)
        {
            if (_windowIntents.Count >= MaxPendingWindowIntents)
                return false;
            _windowIntents.Enqueue(intent);
            return true;
        }
    }

    public bool TryConsumeWindowIntent(out InventoryWindowIntent intent)
    {
        lock (_windowLock)
        {
            if (_windowIntents.Count == 0)
            {
                intent = default;
                return false;
            }

            intent = _windowIntents.Dequeue();
            return true;
        }
    }

    public Player(
        string username,
        NetworkSession session,
        long runtimeId,
        Guid uuid,
        GameMode gameMode = GameMode.Survival,
        bool identityStable = true)
    {
        Username = username;
        Session = session;
        RuntimeId = runtimeId;
        Uuid = uuid;
        IdentityStable = identityStable;
        GameMode = gameMode;
        Inventory = new PlayerInventory(seedStarterHotbar: gameMode == GameMode.Survival);
    }

    public void SubmitMovementInput(in MovementInputState input)
    {
        lock (_movementInputLock)
        {
            _movementInput = input;
        }
    }

    /// <summary>
    /// Peek pending AuthInput without consuming — UseItem may need same-packet sneak (§56).
    /// </summary>
    public bool TryPeekMovementInput(out MovementInputState input)
    {
        lock (_movementInputLock)
        {
            if (!_movementInput.HasValue)
            {
                input = default;
                return false;
            }

            input = _movementInput;
            return true;
        }
    }

    public bool TryConsumeMovementInput(out MovementInputState input)
    {
        lock (_movementInputLock)
        {
            if (!_movementInput.HasValue)
            {
                input = default;
                return false;
            }

            input = _movementInput;
            _movementInput = default;
            return true;
        }
    }

    /// <summary>
    /// Enfileira place/break (FIFO). Cap <see cref="MaxPendingBlockEdits"/>;
    /// overflow rejeita o mais novo (ações já aceites preservam ordem).
    /// </summary>
    public bool SubmitBlockEdit(in BlockEditIntent intent)
    {
        lock (_blockEditLock)
        {
            if (_blockEdits.Count >= MaxPendingBlockEdits)
                return false;
            _blockEdits.Enqueue(intent);
            return true;
        }
    }

    public bool TryConsumeBlockEdit(out BlockEditIntent intent)
    {
        lock (_blockEditLock)
        {
            if (_blockEdits.Count == 0)
            {
                intent = default;
                return false;
            }

            intent = _blockEdits.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Enfileira rearrange ISR (FIFO). Cap <see cref="MaxPendingInventoryStacks"/>;
    /// overflow rejeita o mais novo.
    /// </summary>
    public bool SubmitInventoryStack(in InventoryStackIntent intent)
    {
        lock (_inventoryStackLock)
        {
            if (_inventoryStacks.Count >= MaxPendingInventoryStacks)
                return false;
            _inventoryStacks.Enqueue(intent);
            return true;
        }
    }

    public bool TryConsumeInventoryStack(out InventoryStackIntent intent)
    {
        lock (_inventoryStackLock)
        {
            if (_inventoryStacks.Count == 0)
            {
                intent = default;
                return false;
            }

            intent = _inventoryStacks.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Mensagem já validada pelo ChatProtocol; fan-out no ChatSystem (FIFO, cap
    /// <see cref="MaxPendingChat"/> — overflow rejeita o mais novo, §54).
    /// </summary>
    public bool SubmitChat(string message)
    {
        lock (_chatLock)
        {
            if (_pendingChat.Count >= MaxPendingChat)
                return false;
            _pendingChat.Enqueue(message);
            return true;
        }
    }

    public bool TryConsumeChat(out string message)
    {
        lock (_chatLock)
        {
            if (_pendingChat.Count == 0)
            {
                message = "";
                return false;
            }

            message = _pendingChat.Dequeue();
            return true;
        }
    }

    /// <summary>Overwrite-latest runtime mode (§52). Applied on GameLoop — not by handlers.</summary>
    public void SubmitGameMode(GameMode mode)
    {
        if (IsDead) return;
        lock (_gameModeLock)
            _pendingGameMode = mode;
    }

    public bool TryConsumeGameMode(out GameMode mode)
    {
        lock (_gameModeLock)
        {
            if (_pendingGameMode is null)
            {
                mode = GameMode;
                return false;
            }

            mode = _pendingGameMode.Value;
            _pendingGameMode = null;
            return true;
        }
    }

    /// <summary>GameLoop only. Does not reseed inventory (§52).</summary>
    public void SetGameMode(GameMode mode) => GameMode = mode;

    /// <summary>
    /// Applies an authoritative damage request on the GameLoop. Network handlers and async work
    /// must submit intent/results to that owner rather than mutate health directly.
    /// </summary>
    internal DamageResult ApplyDamage(DamageSource source, float amount) => _health.Apply(source, amount);

    /// <summary>
    /// Finalizes one already-accepted fatal health transition. This is deliberately separate
    /// from <see cref="ApplyDamage"/> so the gameplay owner can first prepare all death-side
    /// effects, including an all-or-nothing survival loot deposit.
    /// </summary>
    internal bool TryFinalizeDeath()
    {
        if (!IsDead || _deathTransitionFinalized) return false;
        _deathTransitionFinalized = true;
        IsSneaking = false;
        IsSprinting = false;
        AbortBreak();
        _ = TryClearOpenContainer(out _);
        lock (_respawnLock)
            _pendingRespawn = false;
        return true;
    }

    /// <summary>Client Respawn CLIENT_READY / PlayerAction RESPAWN — overwrite-latest one-shot.</summary>
    public void SubmitRespawn()
    {
        if (!IsDead) return;
        lock (_respawnLock)
            _pendingRespawn = true;
    }

    public bool TryConsumeRespawn()
    {
        lock (_respawnLock)
        {
            if (!_pendingRespawn) return false;
            _pendingRespawn = false;
            return true;
        }
    }

    /// <summary>Network-to-gameplay handoff for one attack swing; reach is validated on the tick.</summary>
    internal void SubmitAttackIntent()
    {
        lock (_attackIntentLock)
            _pendingAttackIntent = true;
    }

    internal bool TryConsumeAttackIntent()
    {
        lock (_attackIntentLock)
        {
            if (!_pendingAttackIntent) return false;
            _pendingAttackIntent = false;
            return true;
        }
    }

    /// <summary>
    /// Claims one ISR request id exactly once before any authoritative mutation. Kept bounded so
    /// a long-lived session cannot turn replay protection into unbounded memory.
    /// </summary>
    public bool TryClaimInventoryRequest(int requestId)
    {
        if (!_claimedInventoryRequests.Add(requestId))
            return false;

        _claimedInventoryRequestOrder.Enqueue(requestId);
        if (_claimedInventoryRequestOrder.Count > 256)
            _claimedInventoryRequests.Remove(_claimedInventoryRequestOrder.Dequeue());
        return true;
    }

    /// <summary>
    /// Client confirmation after PLAYER_SPAWN. The network thread records only this one-shot;
    /// the gameplay owner performs the visible join/session transition in ChunkStreamSystem.
    /// </summary>
    public void SubmitSpawnReady()
    {
        lock (_spawnReadyLock)
            _pendingSpawnReady = true;
    }

    public bool TryConsumeSpawnReady()
    {
        lock (_spawnReadyLock)
        {
            if (!_pendingSpawnReady) return false;
            _pendingSpawnReady = false;
            return true;
        }
    }

    /// <summary>Clears death after GameLoop applied spawn pose + vitals.</summary>
    public void CompleteRespawn()
    {
        _health.RestoreFull();
        _deathTransitionFinalized = false;
        IsSneaking = false;
        IsSprinting = false;
        LastReplicatedSneaking = false;
        LastReplicatedSprinting = false;
        FallPeakY = PositionY;
        lock (_respawnLock)
            _pendingRespawn = false;
    }
}
