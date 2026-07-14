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

    public string Username { get; }
    public NetworkSession Session { get; }

    /// <summary>Runtime entity id enviado no StartGame / MoveActorAbsolute.</summary>
    public long RuntimeId { get; }

    public float PositionX { get; set; }
    public float PositionY { get; set; } = 8f;
    public float PositionZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }

    public Player(string username, NetworkSession session, long runtimeId)
    {
        Username = username;
        Session = session;
        RuntimeId = runtimeId;
    }

    /// <summary>Somente handlers de rede. Não aplica posição final.</summary>
    public void SubmitMovementInput(in MovementInputState input)
    {
        lock (_movementInputLock)
        {
            _movementInput = input;
        }
    }

    /// <summary>Somente MovementSystem no tick.</summary>
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
}
