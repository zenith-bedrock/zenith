namespace Zenith.LevelDB;

/// <summary>Comparação lexicográfica unsigned de arrays de bytes (ordem de chaves LevelDB).</summary>
sealed class ByteComparer : IComparer<byte[]>
{
    public static ByteComparer Instance { get; } = new();

    public int Compare(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return Compare(x.AsSpan(), y.AsSpan());
    }

    public static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var d = a[i].CompareTo(b[i]);
            if (d != 0) return d;
        }

        return a.Length.CompareTo(b.Length);
    }
}
