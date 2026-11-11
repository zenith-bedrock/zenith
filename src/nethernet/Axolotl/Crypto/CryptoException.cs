namespace Zenith.NetherNet.Axolotl.Crypto;

public class CryptoException : NetherNetException
{
    public CryptoException(string message) : base(message) { }
    public CryptoException(string message, Exception? inner) : base(message, inner) { }
}
