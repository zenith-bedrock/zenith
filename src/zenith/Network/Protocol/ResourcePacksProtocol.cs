using Zenith.Network.Packets;
using Zenith.Network.Session;

namespace Zenith.Network.Protocol;

/// <summary>Transmite info/stack de resource packs. Não decide quais packs aceitar.</summary>
sealed class ResourcePacksProtocol
{
    private readonly NetworkSession _session;

    public ResourcePacksProtocol(NetworkSession session) => _session = session;

    public void SendInfo(bool mustAccept, bool hasAddons, bool hasScripts, string worldTemplateVersion)
    {
        _session.SendDataPacket(new ResourcePacksInfoPacket
        {
            MustAccept = mustAccept,
            HasAddons = hasAddons,
            HasScripts = hasScripts,
            WorldTemplateVersion = worldTemplateVersion
        });
    }

    public void SendStack(bool mustAccept, string gameVersion, bool experimentsPreviouslyToggled, bool hasEditorPacks)
    {
        _session.SendDataPacket(new ResourcePackStackPacket
        {
            MustAccept = mustAccept,
            GameVersion = gameVersion,
            ExperimentsPreviouslyToggled = experimentsPreviouslyToggled,
            HasEditorPacks = hasEditorPacks
        });
    }
}
