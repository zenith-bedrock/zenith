namespace Zenith.NetherNet.Axolotl.Crypto;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Encode(string utf8) => Encode(System.Text.Encoding.UTF8.GetBytes(utf8));

    public static byte[] Decode(string data)
    {
        var remainder = data.Length % 4;
        if (remainder != 0)
            data += new string('=', 4 - remainder);
        try
        {
            return Convert.FromBase64String(data.Replace('-', '+').Replace('_', '/'));
        }
        catch (FormatException e)
        {
            throw new CryptoException("Value is not valid base64url", e);
        }
    }

    public static string DecodeToString(string data) => System.Text.Encoding.UTF8.GetString(Decode(data));
}
