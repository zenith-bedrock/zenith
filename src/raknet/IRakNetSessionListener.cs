using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet;

public interface IRakNetSessionListener
{
    /// <summary>Chamado assim que uma sessão termina o handshake e é registrada no servidor.</summary>
    void OnSessionOpen(RakNetSession session) { }

    /// <summary>
    /// Chamado quando uma sessão é encerrada, seja por pedido do cliente, do servidor ou
    /// por timeout. Garantido disparar no máximo uma vez por sessão.
    /// </summary>
    void OnSessionClose(RakNetSession session, DisconnectReason reason) { }

    bool HandleGamePacket(RakNetSession session, ref BinaryStream stream);
}