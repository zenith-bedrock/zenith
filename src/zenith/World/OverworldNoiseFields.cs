using System.Collections.Concurrent;
using Zenith.World.Noise;

namespace Zenith.World;

/// <summary>
/// Per-seed Simplex fields for overworld height + biome climate (ADR §71).
/// Cached so column fill does not rebuild permutations every sample.
/// </summary>
static class OverworldNoiseFields
{
    private static readonly ConcurrentDictionary<int, Fields> BySeed = new();

    public sealed class Fields
    {
        /// <summary>Broad hills — PM Normal-ish octaves / expansion.</summary>
        public SimplexNoise Height { get; }

        public SimplexNoise Temperature { get; }
        public SimplexNoise Rainfall { get; }

        public Fields(int seed)
        {
            // Distinct seeds so fields are uncorrelated.
            Height = new SimplexNoise(seed ^ unchecked((int)0xA11CE001u), octaves: 4, persistence: 0.5, expansion: 1.0 / 96.0);
            Temperature = new SimplexNoise(seed ^ unchecked((int)0x7EED0001u), octaves: 2, persistence: 1.0 / 16.0, expansion: 1.0 / 512.0);
            Rainfall = new SimplexNoise(seed ^ unchecked((int)0xA1F00001u), octaves: 2, persistence: 1.0 / 16.0, expansion: 1.0 / 512.0);
        }
    }

    public static Fields For(int seed) => BySeed.GetOrAdd(seed, static s => new Fields(s));

    /// <summary>Test isolation — drop cached fields between cases if needed.</summary>
    internal static void ClearForTests() => BySeed.Clear();
}
