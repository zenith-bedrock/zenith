namespace Zenith.Event;

/// <summary>Publicado depois que o login é aceito e o Player já está registrado no PlayerManager.</summary>
class PlayerLoginEvent
{
    public Player.Player Player { get; }

    public PlayerLoginEvent(Player.Player player) => Player = player;
}

/// <summary>
/// Publicado quando a sessão de transporte de um player conectado é encerrada (disconnect,
/// kick ou timeout). Não é disparado se a conexão cair antes do login terminar - nesse caso
/// nunca chegou a existir um Player pra remover.
/// </summary>
class PlayerQuitEvent
{
    public Player.Player Player { get; }

    /// <summary>
    /// True se o player chegou a ficar InGame (spawn completo) antes de sair — false pra quem
    /// caiu durante login/resource-pack/spawn. Consumidores que anunciam "saiu" pros outros
    /// jogadores devem checar isto; senão anunciam a saída de alguém que ninguém viu entrar.
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
