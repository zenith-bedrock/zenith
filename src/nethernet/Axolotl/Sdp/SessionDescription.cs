namespace Zenith.NetherNet.Axolotl.Sdp;

/// <summary>Immutable line-oriented SDP view (port of <c>SessionDescription</c>).</summary>
public sealed class SessionDescription
{
    public const string AttributeIdentity = "identity";
    public const string AttributeFingerprint = "fingerprint";
    public const string AttributeMaxMessageSize = "max-message-size";
    private const long MaxMessageSizeLimit = 4294967295L;
    private static readonly string[] RequiredMediaAttributes = ["ice-ufrag", "ice-pwd", "setup", AttributeMaxMessageSize];
    public const int MaxLines = 1024;

    private readonly string[] _lines;
    private readonly int _mediaStart;

    private SessionDescription(string[] lines, int mediaStart)
    {
        _lines = lines;
        _mediaStart = mediaStart;
    }

    private static SessionDescription FromLines(IEnumerable<string> lines)
    {
        var arr = lines.ToArray();
        return new SessionDescription(arr, FindMediaStart(arr));
    }

    public static SessionDescription Parse(string sdp)
    {
        var lines = new List<string>();
        foreach (var line in sdp.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
        {
            if (line.Length == 0)
                continue;
            if (lines.Count >= MaxLines)
                throw new SdpException($"Session description has more than {MaxLines} lines");
            lines.Add(line);
        }
        if (lines.Count == 0)
            throw new SdpException("Session description is empty");
        return FromLines(lines);
    }

    private static int FindMediaStart(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith("m=", StringComparison.Ordinal))
                return i;
        return lines.Length;
    }

    public override string ToString() => string.Join("\r\n", _lines) + "\r\n";

    public IReadOnlyList<string> Lines => _lines;

    public IReadOnlyList<string> GetSessionAttributeValues(string name) => AttributeValuesIn(_lines.Take(_mediaStart), name);

    public IReadOnlyList<string> GetMediaAttributeValues(string name) => AttributeValuesIn(_lines.Skip(_mediaStart), name);

    private static List<string> AttributeValuesIn(IEnumerable<string> lines, string name)
    {
        var prefix = "a=" + name + ":";
        var values = new List<string>();
        foreach (var line in lines)
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                values.Add(line[prefix.Length..]);
        return values;
    }

    public Fingerprint GetFingerprint()
    {
        var values = GetMediaAttributeValues(AttributeFingerprint);
        if (values.Count == 0)
            values = GetSessionAttributeValues(AttributeFingerprint);
        if (values.Count == 0)
            throw new SdpException("Session description has no fingerprint attribute");
        if (values.Count != 1)
            throw new SdpException($"Expected exactly one fingerprint attribute, got {values.Count}");
        return Fingerprint.Parse(values[0]);
    }

    public long GetMaxMessageSize()
    {
        var values = GetMediaAttributeValues(AttributeMaxMessageSize);
        if (values.Count == 0)
            throw new SdpException($"Media section is missing the {AttributeMaxMessageSize} attribute");
        if (values.Count != 1)
            throw new SdpException($"Expected exactly one {AttributeMaxMessageSize} attribute, got {values.Count}");
        var value = values[0];
        if (value.Length == 0 || !value.All(char.IsAsciiDigit))
            throw new SdpException($"Attribute {AttributeMaxMessageSize} is not a number: {value}");
        if (!long.TryParse(value, out var size) || size > MaxMessageSizeLimit)
            throw new SdpException($"Attribute {AttributeMaxMessageSize} exceeds 32-bit unsigned integer limit, got {value}");
        if (size <= 1)
            throw new SdpException($"Attribute {AttributeMaxMessageSize} must be greater than 1, got {size}");
        return size;
    }

    public string? GetIdentity() => GetSessionAttributeValues(AttributeIdentity).FirstOrDefault();

    public SessionDescription WithoutIdentity()
    {
        var prefix = "a=" + AttributeIdentity + ":";
        return FromLines(_lines.Where(l => !l.StartsWith(prefix, StringComparison.Ordinal)));
    }

    public SessionDescription WithoutCandidates() =>
        FromLines(_lines.Where(l => !l.StartsWith("a=candidate:", StringComparison.Ordinal) && l != "a=end-of-candidates"));

    public SessionDescription WithoutConnectionAddress() =>
        new(_lines.Select(l => l.StartsWith("c=", StringComparison.Ordinal) ? "c=IN IP4 0.0.0.0" : l).ToArray(), _mediaStart);

    public SessionDescription WithCandidates(IEnumerable<string> candidates)
    {
        var lines = new List<string>();
        int? insertAt = null;
        foreach (var line in _lines)
        {
            if (line.StartsWith("a=candidate:", StringComparison.Ordinal))
            {
                insertAt ??= lines.Count;
                continue;
            }
            if (line == "a=end-of-candidates")
                insertAt ??= lines.Count;
            lines.Add(line);
        }
        var at = insertAt ?? lines.Count;
        var merged = new List<string>(lines.Take(at));
        merged.AddRange(candidates.Select(c => "a=candidate:" + c));
        merged.AddRange(lines.Skip(at));
        return FromLines(merged);
    }

    public SessionDescription WithCandidateLimit(int limit)
    {
        var lines = new List<string>();
        var kept = 0;
        foreach (var line in _lines)
        {
            if (line.StartsWith("a=candidate:", StringComparison.Ordinal) && ++kept > limit)
                continue;
            lines.Add(line);
        }
        return FromLines(lines);
    }

    public int CountCandidates() => _lines.Count(l => l.StartsWith("a=candidate:", StringComparison.Ordinal));

    public SessionDescription WithIdentity(string identity)
    {
        var stripped = WithoutIdentity();
        var merged = new List<string>(stripped._lines.Take(stripped._mediaStart))
        {
            "a=" + AttributeIdentity + ":" + identity,
        };
        merged.AddRange(stripped._lines.Skip(stripped._mediaStart));
        return FromLines(merged);
    }

    public void Validate()
    {
        var mediaSections = _lines.Count(l => l.StartsWith("m=", StringComparison.Ordinal));
        if (mediaSections != 1)
            throw new SdpException($"Expected exactly one media section, got {mediaSections}");
        foreach (var name in RequiredMediaAttributes)
            if (GetMediaAttributeValues(name).Count == 0)
                throw new SdpException($"Media section is missing the {name} attribute");
        GetFingerprint();
        GetMaxMessageSize();
    }
}
