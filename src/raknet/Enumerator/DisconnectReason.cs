namespace Zenith.Raknet.Enumerator;

/// <summary>
/// Motivo pelo qual uma <see cref="RakNetSession"/> foi encerrada. Passado pro
/// <see cref="IRakNetSessionListener.OnSessionClose"/> pra quem estiver ouvindo saber
/// se precisa reagir diferente (ex: não vale a pena tentar salvar dados de um player
/// que sofreu timeout do mesmo jeito que um que deu /logout).
/// </summary>
public enum DisconnectReason
{
    /// <summary>O cliente mandou um pacote de disconnect (saiu do jogo normalmente).</summary>
    ClientDisconnect,

    /// <summary>O servidor encerrou a sessão (kick, shutdown, etc.).</summary>
    ServerDisconnect,

    /// <summary>Nenhum pacote foi recebido do cliente dentro do timeout.</summary>
    Timeout,

    /// <summary>Fila de saída (pendente + aguardando ACK) excedeu o limite por sessão — o peer não
    /// está drenando rápido o bastante, seja por conexão lenta ou tráfego malicioso.</summary>
    OutputBacklogExceeded
}
