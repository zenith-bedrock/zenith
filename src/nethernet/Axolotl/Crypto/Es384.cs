using System.Security.Cryptography;

namespace Zenith.NetherNet.Axolotl.Crypto;

/// <summary>ES384 (ECDSA P-384 / SHA-384) signing and verification producing JOSE signatures.</summary>
public static class Es384
{
    public const string Algorithm = "ES384";

    public static byte[] Sign(byte[] input, ECDsa privateKey)
    {
        try
        {
            var der = privateKey.SignData(input, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);
            return EcdsaSignature.DerToJose(der);
        }
        catch (CryptographicException e)
        {
            throw new CryptoException("Failed to sign: " + e.Message, e);
        }
    }

    public static bool Verify(byte[] input, byte[] signature, ECDsa publicKey)
    {
        var der = EcdsaSignature.JoseToDer(signature);
        try
        {
            return publicKey.VerifyData(input, der, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException e)
        {
            throw new CryptoException("Failed to verify: " + e.Message, e);
        }
    }
}
