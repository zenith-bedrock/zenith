namespace Zenith.NetherNet.Axolotl;

/// <summary>Base type for every NetherNet library failure (port of <c>NetherNetException</c>).</summary>
public class NetherNetException : Exception
{
    public NetherNetException() { }
    public NetherNetException(string message) : base(message) { }
    public NetherNetException(string message, Exception? inner) : base(message, inner) { }
}
