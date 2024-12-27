using Zenith.Raknet.Stream;

namespace Zenith.Raknet;

public interface IRakNetSessionListener
{
    bool HandleGamePacket(RakNetSession session, BinaryStream stream);
}