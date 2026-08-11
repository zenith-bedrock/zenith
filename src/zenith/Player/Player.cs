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

    /// <summary>Euclidean interact reach (blocks) from eye to target center — Fase 3 simple authority.</summary>
    public const float MaxBlockReach = 6f;

    private readonly object _movementInputLock = new();
    private MovementInputState _movementInput;
    private readonly object _blockEditLock = new();
    private readonly Queue<BlockEditIntent> _blockEdits = new();
    private readonly object _inventoryStackLock = new();
    private readonly Queue<InventoryStackIntent> _inventoryStacks = new();
    private readonly object _digLock = new();
    private readonly Queue<DigIntent> _digIntents = new();
    private DigIntent? _provisionalDig;
    private readonly object _windowLock = new();
    private readonly Queue<InventoryWindowIntent> _windowIntents = new();
    private readonly object _chatLock = new();
    private readonly Queue<string> _pendingChat = new();
    private readonly object _gameModeLock = new();
    private GameMode? _pendingGameMode;
    private readonly object _respawnLock = new();
    private bool _pendingRespawn;

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

    /// <summary>True após SetLocalPlayerAsInitialized → InGame.</summary>
    public bool IsInGame { get; set; }

    /// <summary>
    /// True while SpawnResponse (after PLAYER_SPAWN, before InGame). Allows ChunkStream
    /// to fill the view ring during loading (ADR §70).
    /// </summary>
    public bool IsSpawning { get; set; }

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

    /// <summary>
    /// Domain vitals (ADR §40) — spawn attributes read these. Fall damage authority shipped
    /// in §96; hunger/drowning/other damage sources remain Deferred.
    /// </summary>
    public float Health { get; set; } = 20f;
    public float Hunger { get; set; } = 20f;

    /// <summary>Highest feet Y reached since last on-ground (ADR §96 fall damage). Reset on landing.</summary>
    public float FallPeakY { get; set; } = Blocks.FlatSpawnY;

    /// <summary>True while death screen is up — AuthInput/edits ignored until respawn tick (§40).</summary>
    public bool IsDead { get; private set; }

    /// <summary>Cause string last sent via DeathInfo (tests / Debug).</summary>
    public string DeathCause { get; private set; } = "";

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
    public int BreakTargetX { get; private set; }
    public int BreakTargetY { get; private set; }
    public int BreakTargetZ { get; private set; }
    public ulong BreakStartedTick { get; private set; }
    /// <summary>Dig duration snapshotted at <see cref="BeginBreak"/> (GameLoop ticks).</summary>
    public int BreakRequiredTicks { get; private set; }
    /// <summary>Held <see cref="StackId"/> at dig start / last retarget (ADR §55).</summary>
    public StackId DigHeldStackId { get; private set; }
    public bool HasBreakTarget { get; private set; }

    /// <summary>
    /// Last GameClock tick that saw start/crack/continue for the dig target.
    /// After <see cref="BreakRequiredTicks"/> + <see cref="DigIdleAbortTicks"/> without activity → StopCrack.
    /// </summary>
    public ulong LastDigActivityTick { get; private set; }

    /// <summary>
    /// Post-dig-window grace without dig AuthInput before aborting crack (~2s @ 20 TPS).
    /// Idle abort must not fire before <see cref="BreakRequiredTicks"/> elapse (§27).
    /// </summary>
    public const ulong DigIdleAbortTicks = 40;

    /// <summary>Open chest UI (ADR §56) — primary + optional partner; SlotCount 27|54.</summary>
    public OpenChestView? OpenChest { get; set; }

    /// <summary>Player inventory UI open (ContainerOpen window 0).</summary>
    public bool InventoryWindowOpen { get; set; }

    /// <summary>Ephemeral 2×2 craft grid — not persisted.</summary>
    public PlayerCraftUi CraftUi { get; } = new();

    public void BeginBreak(int x, int y, int z, ulong tick, int requiredTicks, StackId heldStackId = default)
    {
        BreakTargetX = x;
        BreakTargetY = y;
        BreakTargetZ = z;
        BreakStartedTick = tick;
        BreakRequiredTicks = requiredTicks;
        DigHeldStackId = heldStackId;
        HasBreakTarget = true;
        LastDigActivityTick = tick;
    }

    /// <summary>Refresh dig activity (same-cell crack/continue) so idle abort does not fire.</summary>
    public void MarkDigActive(ulong tick)
    {
        if (!HasBreakTarget) return;
        LastDigActivityTick = tick;
    }

    /// <summary>Progress-preserving dig retarget when held tool changes mid-break (ADR §27).</summary>
    public void RetargetBreakTiming(ulong startedTick, int requiredTicks, StackId heldStackId)
    {
        if (!HasBreakTarget) return;
        BreakStartedTick = startedTick;
        BreakRequiredTicks = requiredTicks;
        DigHeldStackId = heldStackId;
    }

    public void AbortBreak()
    {
        HasBreakTarget = false;
        BreakRequiredTicks = 0;
        DigHeldStackId = default;
        LastDigActivityTick = 0;
        lock (_digLock)
            _provisionalDig = null;
    }

    /// <summary>
    /// Clears dig lock without crack fan-out — used after queueing a Survival break so
    /// Continue can retarget without poisoning the pending intent (§27).
    /// </summary>
    public void ClearBreakTarget()
    {
        AbortBreak();
        lock (_digLock)
            _provisionalDig = null;
    }

    public bool IsBreakTarget(int x, int y, int z) =>
        HasBreakTarget && BreakTargetX == x && BreakTargetY == y && BreakTargetZ == z;

    /// <summary>
    /// Dig auth for same-packet Predict before tick applies <see cref="BeginBreak"/> (§27/§54).
    /// </summary>
    public bool TryGetDigAuth(int x, int y, int z, out ulong startedTick, out int requiredTicks)
    {
        if (IsBreakTarget(x, y, z))
        {
            startedTick = BreakStartedTick;
            requiredTicks = BreakRequiredTicks;
            return true;
        }

        lock (_digLock)
        {
            if (_provisionalDig is { HasValue: true, IsAbort: false } dig &&
                dig.X == x && dig.Y == y && dig.Z == z)
            {
                startedTick = dig.StartedTick;
                requiredTicks = dig.RequiredTicks;
                return true;
            }
        }

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
            return true;
        }
    }

    /// <summary>
    /// Queue dig abort (handler). Clears live dig lock + provisional immediately so same-packet
    /// stale Predict rejects (§27); crack Stop still fans on tick.
    /// </summary>
    public bool SubmitDigAbort(int x, int y, int z)
    {
        if (IsBreakTarget(x, y, z))
            AbortBreak();

        var intent = DigIntent.Abort(x, y, z);
        lock (_digLock)
        {
            if (_digIntents.Count >= MaxPendingDig)
                return false;
            _digIntents.Enqueue(intent);
            _provisionalDig = null;
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
    }

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
    /// Marks dead on the GameLoop. No-op if already dead.
    /// Survival death loot is applied by the caller before this (§73); inventory may already be empty.
    /// </summary>
    public bool BeginDeath(string cause = "generic")
    {
        if (IsDead) return false;
        IsDead = true;
        Health = 0f;
        DeathCause = cause;
        IsSneaking = false;
        IsSprinting = false;
        AbortBreak();
        OpenChest = null;
        InventoryWindowOpen = false;
        CraftUi.Clear();
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

    /// <summary>Clears death after GameLoop applied spawn pose + vitals.</summary>
    public void CompleteRespawn()
    {
        IsDead = false;
        Health = 20f;
        DeathCause = "";
        IsSneaking = false;
        IsSprinting = false;
        LastReplicatedSneaking = false;
        LastReplicatedSprinting = false;
        FallPeakY = PositionY;
        lock (_respawnLock)
            _pendingRespawn = false;
    }
}
