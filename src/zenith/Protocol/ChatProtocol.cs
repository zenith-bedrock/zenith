using Zenith.Packets;
using Zenith.Session;
using Zenith.Raknet.Extension;

namespace Zenith.Protocol;

/// <summary>Transmite chat. Comandos `/` não passam aqui — ver §52 / GameModeSystem.</summary>
sealed class ChatProtocol
{
    private readonly NetworkSession _session;
    private readonly TokenBucketRateLimiter<Guid> _rateLimiter;
    private readonly int _maxMessageLength;

    public ChatProtocol(NetworkSession session)
    {
        _session = session;
        var chat = session.Context.Config.Chat;
        _maxMessageLength = chat.MaxLength;
        _rateLimiter = new TokenBucketRateLimiter<Guid>(chat.RateCapacity, chat.RateRefillPerSecond);
    }

    public int MaxMessageLength => _maxMessageLength;

    public void SendChat(string sourceName, string message, string xboxUserId = "") =>
        _session.SendDataPacket(CreateChatPacket(sourceName, message, xboxUserId));

    /// <summary>Server-authored line (no player source) — join/leave announcements, etc.</summary>
    public void SendSystem(string message) =>
        _session.SendDataPacket(new TextPacket
        {
            Type = TextPacket.TypeSystem,
            NeedsTranslation = false,
            Message = message,
            XboxUserId = "",
            PlatformChatId = "",
            FilteredMessage = null
        });

    /// <summary>
    /// Valida mensagem e rate do jogador dono desta sessão.
    /// Retorna false se vazia, longa demais ou flood.
    /// </summary>
    public bool TryAcceptOutboundChat(Guid playerUuid, string message, out string accepted)
    {
        accepted = message.Trim();
        if (accepted.Length == 0) return false;
        if (accepted.Length > _maxMessageLength) return false;
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

    public static string ClampMessage(string message, int maxLength = 512)
    {
        if (string.IsNullOrEmpty(message)) return "";
        return message.Length <= maxLength
            ? message
            : message[..maxLength];
    }
}
