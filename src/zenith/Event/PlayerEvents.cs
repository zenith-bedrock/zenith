namespace Zenith.Event;

/// <summary>
/// Connection-lifecycle notice, published on the network thread when an accepted session closes (disconnect, kick or timeout).
/// It carries the live <see cref="Player"/> and exists for the one internal subscriber that must release that player's chunk
/// residency from the connection thread. It is NOT a gameplay fact: observers use <see cref="PlayerEnteredWorld"/> and
/// <see cref="PlayerLeftWorld"/>, which are published by the game loop and carry values only.
/// </summary>
class PlayerQuitEvent
{
    public Player.Player Player { get; }

    /// <summary>
    /// True se o player chegou a ficar InGame (spawn completo) antes de sair — false para
    /// quem caiu durante resource-pack/pre-spawn/spawn. Este é um predicado histórico da
    /// transição de gameplay, não uma segunda causa de fechamento de transporte.
    /// </summary>
    public bool WasInGame { get; }

    public PlayerQuitEvent(Player.Player player, bool wasInGame)
    {
        Player = player;
        WasInGame = wasInGame;
    }
}

/// <summary>
/// Protocol negotiate (ADR §43). Pré-preenchido pelo gate; listeners podem mutar
/// <see cref="Accepted"/> (EventBus sem cancel framework — objeto mutável).
/// Accepting a protocol without a matching encode path is unsupported footgun.
/// </summary>
sealed class ProtocolNegotiateEvent
{
    public int ClientProtocol { get; }
    public int ServerProtocol { get; }
    public bool Accepted { get; set; }
    public int RejectPlayStatus { get; set; }

    public ProtocolNegotiateEvent(int clientProtocol, int serverProtocol, bool accepted, int rejectPlayStatus)
    {
        ClientProtocol = clientProtocol;
        ServerProtocol = serverProtocol;
        Accepted = accepted;
        RejectPlayStatus = rejectPlayStatus;
    }
}
