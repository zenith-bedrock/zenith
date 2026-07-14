using Zenith.Network.Packets;
using Zenith.Network.Session;
using Zenith.Raknet.Extension;

namespace Zenith.Network.Protocol;

/// <summary>Transmite chat. Sem comandos; higiene de flood/tamanho neste módulo.</summary>
sealed class ChatProtocol
{
    public const int MaxMessageLength = 512;

    private readonly NetworkSession _session;
    private readonly TokenBucketRateLimiter<Guid> _rateLimiter =
        new(capacity: 8, refillPerSecond: 4);

    public ChatProtocol(NetworkSession session) => _session = session;

    public void SendChat(string sourceName, string message, string xboxUserId = "") =>
        _session.SendDataPacket(CreateChatPacket(sourceName, message, xboxUserId));

    /// <summary>
    /// Valida mensagem e rate do jogador dono desta sessão.
    /// Retorna false se vazia, longa demais ou flood.
    /// </summary>
    public bool TryAcceptOutboundChat(Guid playerUuid, string message, out string accepted)
    {
        accepted = message.Trim();
        if (accepted.Length == 0) return false;
        if (accepted.Length > MaxMessageLength) return false;
        if (!_rateLimiter.TryConsume(playerUuid)) return false;
        return true;
    }

    public static TextPacket CreateChatPacket(string sourceName, string message, string xboxUserId = "") =>
        new()
        {
            Type = TextPacket.TypeChat,
            NeedsTranslation = false,
            SourceName = sourceName,
            Message = message,
            XboxUserId = xboxUserId,
            PlatformChatId = "",
            FilteredMessage = null
        };

    public static string ClampMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        return message.Length <= MaxMessageLength
            ? message
            : message[..MaxMessageLength];
    }
}
