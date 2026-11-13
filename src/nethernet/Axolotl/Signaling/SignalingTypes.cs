using Zenith.NetherNet.Axolotl.Negotiation;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Utils;
using System.Globalization;

namespace Zenith.NetherNet.Axolotl.Signaling;

public class SignalingException : NetherNetException
{
    public SignalingException(string message) : base(message) { }
    public SignalingException(string message, Exception? inner) : base(message, inner) { }
}

public enum SignalType
{
    ConnectRequest,
    ConnectResponse,
    CandidateAdd,
    ConnectError,
}

public static class SignalTypeExtensions
{
    public static string ToWire(this SignalType type) => type switch
    {
        SignalType.ConnectRequest => "CONNECTREQUEST",
        SignalType.ConnectResponse => "CONNECTRESPONSE",
        SignalType.CandidateAdd => "CANDIDATEADD",
        SignalType.ConnectError => "CONNECTERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static SignalType? TryFromWire(string value) => value switch
    {
        "CONNECTREQUEST" => SignalType.ConnectRequest,
        "CONNECTRESPONSE" => SignalType.ConnectResponse,
        "CANDIDATEADD" => SignalType.CandidateAdd,
        "CONNECTERROR" => SignalType.ConnectError,
        _ => null,
    };
}

/// <summary>A <c>TYPE connectionId data</c> signaling message (port of <c>Signal</c>).</summary>
public sealed record Signal(SignalType Type, string ConnectionId, string Data)
{
    private const string MaxConnectionId = "18446744073709551615";

    public static Signal Parse(string message)
    {
        var parts = message.Split(' ', 3);
        if (parts.Length < 3)
            throw new SignalingException("Signaling message must have at least three space-separated fields");

        var type = SignalTypeExtensions.TryFromWire(parts[0])
            ?? throw new SignalingException($"Unknown signaling message type '{parts[0]}'");
        return new Signal(type, ParseConnectionId(parts[1]), parts[2]);
    }

    private static string ParseConnectionId(string field)
    {
        var digitCount = 0;
        while (digitCount < field.Length && char.IsAsciiDigit(field[digitCount]))
            digitCount++;
        if (digitCount == 0)
            throw new SignalingException("Connection ID is not a number");

        var id = field[..digitCount].TrimStart('0');
        if (id.Length > MaxConnectionId.Length
            || (id.Length == MaxConnectionId.Length && string.CompareOrdinal(id, MaxConnectionId) > 0))
            throw new SignalingException("Connection ID does not fit in 64 bits");
        return id.Length == 0 ? "0" : id;
    }

    public static Signal Error(string connectionId, ErrorCode code) =>
        new(SignalType.ConnectError, connectionId, ((int)code).ToString(CultureInfo.InvariantCulture));

    public override string ToString() => Type.ToWire() + " " + ConnectionId + " " + Data;
}

public enum SignalingState
{
    Created,
    Started,
    ShutDown,
}

public static class SignalingStateExtensions
{
    public static void CheckCanStart(this SignalingState state)
    {
        if (state != SignalingState.Created)
            throw new InvalidOperationException(state == SignalingState.Started
                ? "Signaling transport is already started"
                : "Signaling transport has been shut down and cannot be restarted");
    }
}

public sealed record SignalingContext(INegotiator Negotiator, IAddressBlockList BlockList, INetherNetLogger Logger);

/// <summary>A way for clients to reach the server and exchange SDP (port of <c>Signaling</c>).</summary>
public interface ISignaling
{
    string Describe();
    void Start(SignalingContext context);
    void Tick(double now);
    void Shutdown();
}

public interface ISignalingRegistry
{
    void AddSignaling(ISignaling signaling, bool optional = false);
}
