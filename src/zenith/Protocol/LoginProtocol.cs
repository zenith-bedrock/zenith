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

    /// <summary>PlayStatus LOGIN_FAILED_CLIENT/SERVER — Immediate, on the negotiated compression.</summary>
    public void SendIncompatibleProtocol(int playStatus)
    {
        // Cross-reference audit finding: this used to hardcode PacketCompression.NOT_PRESENT on the
        // (stale, since-disproven) assumption that a protocol rejection always runs before the
        // client enables compression. It doesn't — TryAcceptProtocol is checked AFTER
        // SendNetworkSettings has already run unconditionally (both at the RequestNetworkSettings
        // stage and, transitively, at the later Login stage), so the client has always already
        // switched into "every batch has a leading algorithm byte" mode by the time this sends.
        // Sending NOT_PRESENT here made the client misread the first byte of the uncompressed
        // PlayStatus batch (its own small length-prefix varint) as the algorithm byte instead —
        // observed on a real Bedrock client as "Unknown compression type 5" instead of a clean
        // protocol-mismatch rejection, whenever a client's claimed protocol version didn't match
        // ServerIdentity.ProtocolVersion. Use whatever was actually negotiated, same as SendDisconnect.
        _session.SendDataPacket(
            RakNetSession.Priority.Immediate,
            _session.CompressionAlgorithm,
            new PlayStatusPacket { Status = playStatus });
    }

    /// <summary>Bedrock DisconnectPacket with a visible message (Protocol transmits only).</summary>
    public void SendDisconnect(string message, int reason = DisconnectPacket.ReasonUnknown)
    {
        _session.SendDataPacket(
            RakNetSession.Priority.Immediate,
            _session.CompressionAlgorithm,
            new DisconnectPacket
            {
                Reason = reason,
                HideDisconnectionScreen = false,
                Message = message,
                FilteredMessage = ""
            });
    }
}
