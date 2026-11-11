using System.Security.Cryptography;
using Zenith.NetherNet.Axolotl.Crypto;

namespace Zenith.NetherNet.Axolotl.Identity;

/// <summary>The server's long-lived ES384 identity key (port of <c>ServerIdentity</c>).</summary>
public sealed class ServerIdentity
{
    private ServerIdentity(ECDsa privateKey, PublicKey publicKey)
    {
        PrivateKey = privateKey;
        PublicKey = publicKey;
    }

    public ECDsa PrivateKey { get; }
    public PublicKey PublicKey { get; }

    public static ServerIdentity Generate()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        return new ServerIdentity(key, PublicKey.FromEcdsa(key));
    }

    public static ServerIdentity FromPrivateKeyPem(string pem, string? passphrase = null)
    {
        var key = ECDsa.Create();
        try
        {
            if (passphrase is null)
                key.ImportFromPem(pem);
            else
                key.ImportFromEncryptedPem(pem, passphrase);
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException)
        {
            key.Dispose();
            throw new IdentityException("The private key could not be read: " + e.Message, e);
        }
        return new ServerIdentity(key, PublicKey.FromEcdsa(key));
    }

    public byte[] Sign(byte[] input)
    {
        try
        {
            return Es384.Sign(input, PrivateKey);
        }
        catch (CryptoException e)
        {
            throw new IdentityException(e.Message, e);
        }
    }

    public byte[] Sign(string input) => Sign(System.Text.Encoding.UTF8.GetBytes(input));

    /// <summary>PKCS#8 PEM with the key encoded exactly like the reference exporter.</summary>
    public string ExportPrivateKeyPem()
    {
        var (d, x, y) = ReadComponents();
        var ecPrivateKey = Der.Sequence(Der.Concat(
            Der.Integer([0x01]),
            Der.OctetString(d),
            Der.Tagged(1, Der.BitString(Der.Concat([0x04], x, y)))));
        var der = Der.Sequence(Der.Concat(
            Der.Integer([0x00]),
            Der.Sequence(P384.AlgorithmIdentifier),
            Der.OctetString(ecPrivateKey)));
        return "-----BEGIN PRIVATE KEY-----\n"
            + Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks).Replace("\r\n", "\n") + "\n"
            + "-----END PRIVATE KEY-----\n";
    }

    private (byte[] D, byte[] X, byte[] Y) ReadComponents()
    {
        ECParameters p;
        try
        {
            p = PrivateKey.ExportParameters(true);
        }
        catch (CryptographicException e)
        {
            throw new IdentityException("Failed to extract key components: " + e.Message, e);
        }
        if (p.D is null || p.Q.X is null || p.Q.Y is null)
            throw new IdentityException("The key is missing a component");
        try
        {
            return (P384.PadCoordinate(p.D), P384.PadCoordinate(p.Q.X), P384.PadCoordinate(p.Q.Y));
        }
        catch (CryptoException e)
        {
            throw new IdentityException(e.Message, e);
        }
    }
}
