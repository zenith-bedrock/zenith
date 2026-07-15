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

    /// <summary>Euclidean interact reach (blocks) from eye to target center — Fase 3 simple authority.</summary>
    public const float MaxBlockReach = 6f;

    private readonly object _movementInputLock = new();
    private MovementInputState _movementInput;
    private readonly object _blockEditLock = new();
    private readonly Queue<BlockEditIntent> _blockEdits = new();
    private readonly object _inventoryStackLock = new();
    private readonly Queue<InventoryStackIntent> _inventoryStacks = new();
    private readonly object _chatLock = new();
    private string? _pendingChat;

    public string Username { get; }
    public NetworkSession Session { get; }

    /// <summary>Runtime entity id enviado no StartGame / MoveActorAbsolute.</summary>
    public long RuntimeId { get; }

    /// <summary>UUID de lista/AddPlayer.</summary>
    public Guid Uuid { get; }

    /// <summary>True após SetLocalPlayerAsInitialized → InGame.</summary>
    public bool IsInGame { get; set; }

    /// <summary>Hotbar 0–8; selected slot bounds-checked no handler.</summary>
    public int SelectedHotbarSlot { get; set; }

    public PlayerInventory Inventory { get; }

    /// <summary>Join-time mode from config (ADR §31). Not mutated by handlers.</summary>
    public GameMode GameMode { get; }

    /// <summary>Colunas enviadas / em voo e raio de view (streaming).</summary>
    public PlayerChunkTracker Chunks { get; } = new();

    /// <summary>Runtime id do bloco no slot selecionado (via inventário).</summary>
    public int HeldBlockRuntimeId => Inventory.GetRuntimeId(SelectedHotbarSlot);

    /// <summary>Skin RGBA opcional parseada do login (senão PlayerList usa placeholder).</summary>
    public byte[]? SkinRgba { get; set; }
    public uint SkinWidth { get; set; }
    public uint SkinHeight { get; set; }

    public float PositionX { get; set; }
    public float PositionY { get; set; } = Blocks.FlatSpawnY;
    public float PositionZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }

    /// <summary>Domain vitals seed (ADR §40) — spawn attributes read these; damage Deferred.</summary>
    public float Health { get; set; } = 20f;
    public float Hunger { get; set; } = 20f;

    /// <summary>Último held replicado a peers (EquipmentSystem).</summary>
    public int LastReplicatedHotbarSlot { get; set; } = -1;
    public int LastReplicatedHeldRuntimeId { get; set; } = int.MinValue;
    public int LastReplicatedHeldCount { get; set; } = int.MinValue;

    /// <summary>Última pose enviada a peers via MoveActorAbsolute (MovementSystem dirty-check).</summary>
    public float LastReplicatedX { get; set; }
    public float LastReplicatedY { get; set; }
    public float LastReplicatedZ { get; set; }
    public float LastReplicatedPitch { get; set; }
    public float LastReplicatedYaw { get; set; }
    public float LastReplicatedHeadYaw { get; set; }

    /// <summary>Server-authoritative break progress (AuthInput start → predict). Cleared on abort/success.</summary>
    public int BreakTargetX { get; private set; }
    public int BreakTargetY { get; private set; }
    public int BreakTargetZ { get; private set; }
    public ulong BreakStartedTick { get; private set; }
    /// <summary>Empty-hand dig duration snapshotted at <see cref="BeginBreak"/> (GameLoop ticks).</summary>
    public int BreakRequiredTicks { get; private set; }
    public bool HasBreakTarget { get; private set; }

    /// <summary>Baú aberto (UI) — slots no <see cref="World.ChestStore"/>; limpar no ContainerClose.</summary>
    public (int X, int Y, int Z)? OpenChest { get; set; }

    public void BeginBreak(int x, int y, int z, ulong tick, int requiredTicks)
    {
        BreakTargetX = x;
        BreakTargetY = y;
        BreakTargetZ = z;
        BreakStartedTick = tick;
        BreakRequiredTicks = requiredTicks;
        HasBreakTarget = true;
    }

    public void AbortBreak()
    {
        HasBreakTarget = false;
        BreakRequiredTicks = 0;
    }

    public bool IsBreakTarget(int x, int y, int z) =>
        HasBreakTarget && BreakTargetX == x && BreakTargetY == y && BreakTargetZ == z;

    public Player(
        string username,
        NetworkSession session,
        long runtimeId,
        Guid uuid,
        GameMode gameMode = GameMode.Survival)
    {
        Username = username;
        Session = session;
        RuntimeId = runtimeId;
        Uuid = uuid;
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

    /// <summary>Mensagem já validada pelo ChatProtocol; fan-out no ChatSystem.</summary>
    public void SubmitChat(string message)
    {
        lock (_chatLock)
        {
            _pendingChat = message;
        }
    }

    public bool TryConsumeChat(out string message)
    {
        lock (_chatLock)
        {
            if (_pendingChat is null)
            {
                message = "";
                return false;
            }

            message = _pendingChat;
            _pendingChat = null;
            return true;
        }
    }
}
