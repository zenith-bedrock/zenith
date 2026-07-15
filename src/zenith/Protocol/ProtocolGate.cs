namespace Zenith.Protocol;

/// <summary>
/// Pure protocol acceptance (ADR §43). Caller passes <see cref="Zenith.Server.ServerIdentity.ProtocolVersion"/>.
/// Does not reference encode codecs — Accepted ≠ multiprotocol support.
/// </summary>
static class ProtocolGate
{
    public enum Outcome
    {
        Accepted,
        FailClient,
        FailServer
    }

    public static Outcome Evaluate(int clientProtocol, int serverProtocol)
    {
        if (clientProtocol == serverProtocol) return Outcome.Accepted;
        return clientProtocol < serverProtocol ? Outcome.FailClient : Outcome.FailServer;
    }

    public static int RejectPlayStatus(Outcome outcome) => outcome switch
    {
        Outcome.FailClient => Packets.PlayStatusPacket.LoginFailedClient,
        Outcome.FailServer => Packets.PlayStatusPacket.LoginFailedServer,
        _ => Packets.PlayStatusPacket.LoginFailedClient
    };
}
