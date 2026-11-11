namespace Zenith.NetherNet.Axolotl.Protocol;

public enum Reliability
{
    Reliable,
    Unreliable,
}

public static class ReliabilityExtensions
{
    public static string GetChannelLabel(this Reliability r) => r switch
    {
        Reliability.Reliable => "ReliableDataChannel",
        Reliability.Unreliable => "UnreliableDataChannel",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    public static bool IsFragmentationSupported(this Reliability r) => r == Reliability.Reliable;

    public static Reliability? FromChannelLabel(string label) => label switch
    {
        "ReliableDataChannel" => Reliability.Reliable,
        "UnreliableDataChannel" => Reliability.Unreliable,
        _ => null,
    };
}
