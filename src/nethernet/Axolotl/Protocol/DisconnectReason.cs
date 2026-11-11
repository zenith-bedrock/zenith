namespace Zenith.NetherNet.Axolotl.Protocol;

public enum DisconnectReason
{
    PeerDisconnect = 0,
    ServerDisconnect = 1,
    ServerShutdown = 2,
    RejectedByHost = 3,
    ConnectionFailed = 4,
    BadData = 5,
    SendFailed = 6,
    ReceiveQueueTooManyBytes = 7,
    ReceiveQueueTooManyMessages = 8,
    SendQueueTooManyBytes = 9,
    AddressBlocked = 10,
}

public static class DisconnectReasonExtensions
{
    public static string GetMessage(this DisconnectReason reason) => reason switch
    {
        DisconnectReason.PeerDisconnect => "client disconnect",
        DisconnectReason.ServerDisconnect => "server disconnect",
        DisconnectReason.ServerShutdown => "server shutdown",
        DisconnectReason.RejectedByHost => "rejected by the host",
        DisconnectReason.ConnectionFailed => "connection failed",
        DisconnectReason.BadData => "bad data received",
        DisconnectReason.SendFailed => "failed to send",
        DisconnectReason.ReceiveQueueTooManyBytes => "too many unread bytes queued",
        DisconnectReason.ReceiveQueueTooManyMessages => "too many unread messages queued",
        DisconnectReason.SendQueueTooManyBytes => "too many unsent bytes queued",
        DisconnectReason.AddressBlocked => "address blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
