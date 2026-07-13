using Zenith.Session;

namespace Zenith.Player;

/// <summary>
/// Entidade lógica de um jogador conectado. Não sabe nada sobre raknet nem sobre o wire
/// format do protocolo Bedrock; fala com o cliente só através da <see cref="NetworkSession"/>
/// que a criou. É aqui que estado de jogo (posição, inventário, world atual, ...) vai morar
/// conforme World/Inventory forem existindo.
/// </summary>
class Player
{
    public string Username { get; }
    public NetworkSession Session { get; }

    public Player(string username, NetworkSession session)
    {
        Username = username;
        Session = session;
    }
}
