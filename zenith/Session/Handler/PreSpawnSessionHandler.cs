using Zenith.Raknet.Stream;
using zenith.Network.Protocol;

namespace zenith.Session.Handler;

/// <summary>
/// Placeholder pro estado entre "cliente carregou os dados do StartGamePacket" e
/// "player efetivamente spawnado". É aqui que chunk sending e spawn packets vão entrar
/// assim que existir Player/World. Por enquanto só marca a transição.
/// </summary>
class PreSpawnSessionHandler : ISessionHandler
{
    public void OnEnable(NetworkSession session)
    {
        Console.WriteLine("Session entered pre-spawn stage (not yet implemented).");
    }

    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, BinaryStream stream) => false;
}
