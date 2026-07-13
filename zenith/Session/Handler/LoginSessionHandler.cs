using System.Text;
using System.Text.Json;
using Zenith.Raknet.Stream;
using Zenith.Event;
using Zenith.Network.Protocol;

namespace Zenith.Session.Handler;

/// <summary>
/// Primeiro estado de toda conexão: negociação de network settings e login. Handler inicial
/// atribuído a toda <see cref="NetworkSession"/> nova. Ao final do login com sucesso, troca
/// pra <see cref="ResourcePacksSessionHandler"/>.
/// </summary>
class LoginSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        switch (header.Id)
        {
            case (int)ProtocolInfo.REQUEST_NETWORK_SETTINGS_PACKET:
                HandleRequestNetworkSettings(session, ref stream);
                return true;
            case (int)ProtocolInfo.LOGIN_PACKET:
                HandleLogin(session, ref stream);
                return true;
            default:
                return false;
        }
    }

    private static void HandleRequestNetworkSettings(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<RequestNetworkSettingsPacket>(ref stream);
        session.Context.Logger.Debug($"RequestNetworkSettingsPacket: {packet.ProtocolVersion}");

        // TODO: recusar a conexão aqui se packet.ProtocolVersion não for suportado,
        // em vez de deixar seguir com um protocolo desconhecido.

        var settings = new NetworkSettingsPacket
        {
            CompressionThreshold = 256,
            CompressionAlgorithm = PacketCompression.ZLIB,
            EnableClientThrottling = false,
            ClientThrottleThreshold = 0,
            ClientThrottleScalar = 0
        };

        session.CompressionAlgorithm = PacketCompression.ZLIB;
        session.SendDataPacket(Zenith.Raknet.RakNetSession.Priority.Normal, PacketCompression.NOT_PRESENT, settings);
    }

    private static void HandleLogin(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<LoginPacket>(ref stream);
        session.Context.Logger.Debug($"LoginPacket: {packet.Protocol}");

        string username;
        try
        {
            username = ExtractDisplayName(packet.AuthInfo);
        }
        catch (Exception ex)
        {
            session.Context.Logger.Warning($"Failed to parse identity chain: {ex.Message}");
            session.Disconnect();
            return;
        }

        // TODO: validar a assinatura da chain contra a chave raiz da Mojang antes de confiar
        // no displayName - hoje qualquer cliente pode se declarar com qualquer nome/uuid.
        // Suficiente pra desenvolvimento local, não serve pra produção exposta.

        var player = new Zenith.Player.Player(username, session);
        if (!session.Context.PlayerManager.TryAdd(player))
        {
            session.Context.Logger.Warning($"Rejected login: '{username}' already online.");
            session.Disconnect();
            return;
        }

        session.Player = player;
        session.Context.EventBus.Publish(new PlayerLoginEvent(player));

        var playStatus = new PlayStatusPacket { Status = 0 };
        var resourcePacksInfo = new ResourcePacksInfoPacket
        {
            MustAccept = true,
            HasAddons = false,
            HasScripts = false,
            WorldTemplateVersion = ""
        };

        session.SendDataPacket(playStatus, resourcePacksInfo);
        session.SetHandler(new ResourcePacksSessionHandler());
    }

    /// <summary>
    /// Decodifica o payload do token JWT de autenticação e extrai o claim "xname",
    /// que contém o gamertag do jogador. Não verifica a assinatura criptográfica ainda.
    /// </summary>
    private static string ExtractDisplayName(LoginPacket.AuthenticationInfo authInfo)
    {
        var parts = authInfo.Token.Split('.');
        if (parts.Length < 2) throw new FormatException("Invalid JWT token format.");

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payload = JsonDocument.Parse(payloadJson);

        if (!payload.RootElement.TryGetProperty("xname", out var displayName))
            throw new FormatException("Token does not contain a valid xname claim.");

        var value = displayName.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("The xname claim is empty.");

        return value;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
