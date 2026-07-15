using Zenith.Packets;
using Zenith.Session;
using Zenith.Raknet;

namespace Zenith.Protocol;

/// <summary>Transmite intenções de login/network-settings. Não decide política de versão ou auth.</summary>
sealed class LoginProtocol
{
    private readonly NetworkSession _session;

    public LoginProtocol(NetworkSession session) => _session = session;

    public void SendNetworkSettings(
        short compressionThreshold,
        short compressionAlgorithm,
        bool enableClientThrottling,
        byte clientThrottleThreshold,
        float clientThrottleScalar)
    {
        // Envelope sem compressão: a negociação ocorre antes do cliente habilitar compressão.
        _session.SendDataPacket(
            RakNetSession.Priority.Normal,
            PacketCompression.NOT_PRESENT,
            new NetworkSettingsPacket
            {
                CompressionThreshold = compressionThreshold,
                CompressionAlgorithm = compressionAlgorithm,
                EnableClientThrottling = enableClientThrottling,
                ClientThrottleThreshold = clientThrottleThreshold,
                ClientThrottleScalar = clientThrottleScalar
            });
    }

    public void SendLoginSuccess()
    {
        _session.SendDataPacket(new PlayStatusPacket { Status = PlayStatusPacket.LoginSuccess });
    }

    /// <summary>PlayStatus LOGIN_FAILED_CLIENT/SERVER — no policy; Handler decided.</summary>
    public void SendIncompatibleProtocol(int playStatus)
    {
        _session.SendDataPacket(new PlayStatusPacket { Status = playStatus });
    }
}
