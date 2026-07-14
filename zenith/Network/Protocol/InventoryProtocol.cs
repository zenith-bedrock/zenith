using Zenith.Network.Packets;
using Zenith.Network.Session;
using Zenith.Player;

namespace Zenith.Network.Protocol;

/// <summary>Transmite conteúdo de inventário já decidido pelo gameplay.</summary>
sealed class InventoryProtocol
{
    private readonly NetworkSession _session;

    public InventoryProtocol(NetworkSession session) => _session = session;

    public void SendHotbarContent(PlayerInventory inventory)
    {
        _session.SendDataPacket(new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            Slots = inventory.SnapshotMainInventory()
        });
    }
}
