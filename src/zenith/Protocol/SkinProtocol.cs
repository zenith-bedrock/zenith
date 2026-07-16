using Zenith.Packets;
using Zenith.Session;

namespace Zenith.Protocol;

sealed class SkinProtocol
{
    private readonly NetworkSession _session;

    public SkinProtocol(NetworkSession session) => _session = session;

    public void SendSkin(
        string uuid,
        SerializedSkin skin,
        string skinName,
        string oldSkinName,
        bool isVerified)
    {
        _session.SendDataPacket(new PlayerSkinPacket
        {
            Uuid = uuid,
            Skin = skin,
            SkinName = skinName,
            OldSkinName = oldSkinName,
            IsVerified = isVerified
        });
    }
}
