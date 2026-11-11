using System.Text.RegularExpressions;

namespace Zenith.NetherNet.Axolotl.Sdp;

public class SdpException : NetherNetException
{
    public SdpException(string message) : base(message) { }
}

/// <summary>A DTLS certificate fingerprint as carried in SDP (port of <c>Fingerprint</c>).</summary>
public sealed partial class Fingerprint
{
    private const string AlgorithmName = "sha-256";

    [GeneratedRegex(@"^[0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2})*\z")]
    private static partial Regex DigestPattern();

    public string Algorithm { get; }
    public string Digest { get; }

    public Fingerprint(string algorithm, string digest)
    {
        if (!string.Equals(algorithm, AlgorithmName, StringComparison.OrdinalIgnoreCase))
            throw new SdpException($"Fingerprint algorithm must be {AlgorithmName}, got: {algorithm}");
        if (!DigestPattern().IsMatch(digest))
            throw new SdpException($"Fingerprint digest is not colon-separated hex: {digest}");
        Algorithm = algorithm;
        Digest = digest;
    }

    public static Fingerprint Parse(string value)
    {
        var parts = value.Split(' ', 3);
        if (parts.Length != 2)
            throw new SdpException($"Expected an algorithm and a digest separated by one space, got: {value}");
        return new Fingerprint(parts[0], parts[1]);
    }

    public bool Equals(Fingerprint other) =>
        string.Equals(Algorithm, other.Algorithm, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Digest, other.Digest, StringComparison.OrdinalIgnoreCase);

    public string ToAttributeValue() => Algorithm + " " + Digest;

    /// <summary>The exact JSON string the identity assertion signs.</summary>
    public string ToCanonicalPayload() =>
        "{\"fingerprint\":[{\"algorithm\":\"" + Algorithm + "\",\"digest\":\"" + Digest + "\"}]}";
}
