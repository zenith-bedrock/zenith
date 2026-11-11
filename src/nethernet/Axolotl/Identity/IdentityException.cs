namespace Zenith.NetherNet.Axolotl.Identity;

public class IdentityException : NetherNetException
{
    public IdentityException(string message) : base(message) { }
    public IdentityException(string message, Exception? inner) : base(message, inner) { }
}
