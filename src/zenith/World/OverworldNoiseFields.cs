using System.Collections.Concurrent;
using Zenith.World.Noise;

namespace Zenith.World;

/// <summary>
/// Per-seed FastNoiseLite fields for overworld height + climate (ADR §72).
/// Cached so column fill does not rebuild noise state every sample.
/// </summary>
static class OverworldNoiseFields
{
    private static readonly ConcurrentDictionary<int, Fields> BySeed = new();

    public sealed class Fields
    {
        /// <summary>Broad hills — OpenSimplex2 FBm.</summary>
        public FastNoiseLite Height { get; }

        public FastNoiseLite Temperature { get; }
        public FastNoiseLite Rainfall { get; }

        public Fields(int seed)
        {
            // Distinct seeds so fields are uncorrelated.
            Height = Create(
                seed ^ unchecked((int)0xA11CE001u),
                frequency: 1f / 128f,
                octaves: 3,
                gain: 0.5f);
            Temperature = Create(
                seed ^ unchecked((int)0x7EED0001u),
                frequency: 1f / 512f,
                octaves: 2,
                gain: 0.5f);
            Rainfall = Create(
                seed ^ unchecked((int)0xA1F00001u),
                frequency: 1f / 512f,
                octaves: 2,
                gain: 0.5f);
        }

        private static FastNoiseLite Create(int seed, float frequency, int octaves, float gain)
        {
            var n = new FastNoiseLite(seed);
            n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            n.SetFractalType(FastNoiseLite.FractalType.FBm);
            n.SetFractalOctaves(octaves);
            n.SetFractalLacunarity(2f);
            n.SetFractalGain(gain);
            n.SetFrequency(frequency);
            return n;
        }
    }

    public static Fields For(int seed) => BySeed.GetOrAdd(seed, static s => new Fields(s));

    /// <summary>Test isolation — drop cached fields between cases if needed.</summary>
    internal static void ClearForTests() => BySeed.Clear();

    /// <summary>Map FastNoiseLite ≈[-1,1] to climate [0,1].</summary>
    public static double Climate01(float noise) => Math.Clamp((noise + 1.0) * 0.5, 0.0, 1.0);
}
