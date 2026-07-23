namespace Zenith.World.Noise;

/// <summary>
/// 2D Simplex with octaves (PocketMine-shaped — ADR §71). No NuGet noise libs.
/// Values roughly in [-1, 1] when <paramref name="normalized"/> is false (single octave raw ×70);
/// normalized octave sum maps to approximately [-1, 1].
/// </summary>
sealed class SimplexNoise
{
    private static readonly int[][] Grad3 =
    [
        [1, 1, 0], [-1, 1, 0], [1, -1, 0], [-1, -1, 0],
        [1, 0, 1], [-1, 0, 1], [1, 0, -1], [-1, 0, -1],
        [0, 1, 1], [0, -1, 1], [0, 1, -1], [0, -1, -1]
    ];

    private const double F2 = 0.5 * (1.7320508075688772 - 1.0); // 0.5*(√3-1)
    private const double G2 = (3.0 - 1.7320508075688772) / 6.0;
    private const double G22 = G2 * 2.0 - 1.0;

    private readonly int[] _perm = new int[512];
    private readonly double _offsetX;
    private readonly double _offsetY;
    private readonly int _octaves;
    private readonly double _persistence;
    private readonly double _expansion;

    public SimplexNoise(int seed, int octaves, double persistence, double expansion)
    {
        if (octaves < 1)
            throw new ArgumentOutOfRangeException(nameof(octaves));
        _octaves = octaves;
        _persistence = persistence;
        _expansion = expansion;

        var rng = new Random(seed);
        _offsetX = rng.NextDouble() * 256.0;
        _offsetY = rng.NextDouble() * 256.0;

        var source = new int[256];
        for (var i = 0; i < 256; i++)
            source[i] = i;
        for (var i = 255; i >= 0; i--)
        {
            var j = rng.Next(i + 1);
            (source[i], source[j]) = (source[j], source[i]);
        }

        for (var i = 0; i < 256; i++)
        {
            _perm[i] = source[i];
            _perm[i + 256] = source[i];
        }
    }

    /// <summary>Octaved 2D sample. When <paramref name="normalized"/>, divides by amp sum (~[-1,1]).</summary>
    public double Noise2D(double x, double z, bool normalized = false)
    {
        var result = 0.0;
        var amp = 1.0;
        var freq = 1.0;
        var max = 0.0;

        x *= _expansion;
        z *= _expansion;

        for (var i = 0; i < _octaves; i++)
        {
            result += Raw2D(x * freq, z * freq) * amp;
            max += amp;
            freq *= 2.0;
            amp *= _persistence;
        }

        if (normalized && max > 0)
            result /= max;
        return result;
    }

    private double Raw2D(double x, double y)
    {
        x += _offsetX;
        y += _offsetY;

        var s = (x + y) * F2;
        var i = (int)Math.Floor(x + s);
        var j = (int)Math.Floor(y + s);
        var t = (i + j) * G2;
        var x0 = x - (i - t);
        var y0 = y - (j - t);

        int i1, j1;
        if (x0 > y0)
        {
            i1 = 1;
            j1 = 0;
        }
        else
        {
            i1 = 0;
            j1 = 1;
        }

        var x1 = x0 - i1 + G2;
        var y1 = y0 - j1 + G2;
        var x2 = x0 + G22;
        var y2 = y0 + G22;

        var ii = i & 255;
        var jj = j & 255;
        var n = 0.0;

        var t0 = 0.5 - x0 * x0 - y0 * y0;
        if (t0 > 0)
        {
            var g = Grad3[_perm[ii + _perm[jj]] % 12];
            n += t0 * t0 * t0 * t0 * (g[0] * x0 + g[1] * y0);
        }

        var t1 = 0.5 - x1 * x1 - y1 * y1;
        if (t1 > 0)
        {
            var g = Grad3[_perm[ii + i1 + _perm[jj + j1]] % 12];
            n += t1 * t1 * t1 * t1 * (g[0] * x1 + g[1] * y1);
        }

        var t2 = 0.5 - x2 * x2 - y2 * y2;
        if (t2 > 0)
        {
            var g = Grad3[_perm[ii + 1 + _perm[jj + 1]] % 12];
            n += t2 * t2 * t2 * t2 * (g[0] * x2 + g[1] * y2);
        }

        return 70.0 * n;
    }
}
