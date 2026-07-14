using Zenith.Network.Session;

namespace Zenith.Player;

/// <summary>
/// Entidade lógica de um jogador conectado. Não sabe nada sobre raknet nem wire format.
/// Estado de gameplay (posição) só é mutado no GameLoop; a rede só escreve <see cref="MovementInputState"/>.
/// </summary>
class Player
{
    private readonly object _movementInputLock = new();
    private MovementInputState _movementInput;
    private readonly object _blockEditLock = new();
    private BlockEditIntent _blockEdit;

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

    /// <summary>Runtime id do bloco colocado (creative flat). Air = 0.</summary>
    public int HeldBlockRuntimeId { get; set; } = 1;

    /// <summary>Skin RGBA opcional parseada do login (senão PlayerList usa placeholder).</summary>
    public byte[]? SkinRgba { get; set; }
    public uint SkinWidth { get; set; }
    public uint SkinHeight { get; set; }

    public float PositionX { get; set; }
    public float PositionY { get; set; } = 8f;
    public float PositionZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }

    public Player(string username, NetworkSession session, long runtimeId, Guid uuid)
    {
        Username = username;
        Session = session;
        RuntimeId = runtimeId;
        Uuid = uuid;
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

    public void SubmitBlockEdit(in BlockEditIntent intent)
    {
        lock (_blockEditLock)
        {
            _blockEdit = intent;
        }
    }

    public bool TryConsumeBlockEdit(out BlockEditIntent intent)
    {
        lock (_blockEditLock)
        {
            if (!_blockEdit.HasValue)
            {
                intent = default;
                return false;
            }

            intent = _blockEdit;
            _blockEdit = default;
            return true;
        }
    }
}
