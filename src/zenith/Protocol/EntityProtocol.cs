using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Transmite intenções de entidade. Sem lógica de gameplay. One partial file per concern: Movement, Roster, Equipment, Feedback, Spawns, ActorData, Lifecycle, Ports.</summary>
sealed partial class EntityProtocol : IEntityChannel
{
    private readonly NetworkSession _session;

    public EntityProtocol(NetworkSession session) => _session = session;
}
