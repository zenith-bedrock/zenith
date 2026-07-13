using Zenith.Raknet.Stream;
using zenith.Network.Protocol;

namespace zenith.Session.Handler;

/// <summary>
/// Representa um estado no ciclo de vida de conexão de uma <see cref="NetworkSession"/>
/// (login, resource packs, pre-spawn, in-game, ...). Só um handler fica ativo por vez;
/// a sessão troca de handler conforme o cliente avança no fluxo de conexão.
///
/// Isso substitui o switch monolítico que existia no SessionListener original: cada estado
/// da conexão vira sua própria classe, só conhecendo os pacotes que fazem sentido pra ela.
/// </summary>
interface ISessionHandler
{
    /// <summary>
    /// Tenta tratar o pacote recebido. Retorna false se esse handler não sabe lidar com o id
    /// do pacote (a sessão loga como "unhandled" nesse caso).
    /// </summary>
    bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, BinaryStream stream);

    /// <summary>Chamado assim que esse handler se torna o handler ativo da sessão.</summary>
    void OnEnable(NetworkSession session) { }

    /// <summary>Chamado logo antes desse handler ser substituído por outro.</summary>
    void OnDisable(NetworkSession session) { }
}
