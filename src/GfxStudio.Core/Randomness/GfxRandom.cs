using GfxStudio.Core.Randomness.Seeding;

namespace GfxStudio.Core.Randomness;

/// <summary>
/// Xoshiro256** generator with a fixed algorithm and deterministic, scope-based forks.
/// </summary>
public sealed class GfxRandom : IGfxRandom
{
    private const double Inverse53BitRange = 1.0 / 9007199254740992.0;
    private const float Inverse24BitRange = 1.0f / 16777216.0f;

    private readonly ulong _seed;
    private ulong _state0;
    private ulong _state1;
    private ulong _state2;
    private ulong _state3;
    private bool _hasGaussianSample;
    private double _gaussianSample;

    public GfxRandom(ulong seed)
    {
        _seed = seed;
        var splitter = new SplitMix64(seed);
        _state0 = splitter.NextUInt64();
        _state1 = splitter.NextUInt64();
        _state2 = splitter.NextUInt64();
        _state3 = splitter.NextUInt64();

        // Xoshiro's all-zero state is absorbing. SplitMix64 should not produce it, but
        // keep the invariant explicit in case the initialization is changed later.
        if ((_state0 | _state1 | _state2 | _state3) == 0)
        {
            _state0 = 1;
        }
    }

    public ulong NextUInt64()
    {
        unchecked
        {
            var result = RotateLeft(_state1 * 5, 7) * 9;
            var temporary = _state1 << 17;

            _state2 ^= _state0;
            _state3 ^= _state1;
            _state1 ^= _state2;
            _state0 ^= _state3;
            _state2 ^= temporary;
            _state3 = RotateLeft(_state3, 45);

            return result;
        }
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "The exclusive maximum must be greater than the inclusive minimum.");
        }

        var range = (ulong)((long)maxExclusive - minInclusive);
        var rejectionThreshold = unchecked(0UL - range) % range;
        ulong sample;
        do
        {
            sample = NextUInt64();
        }
        while (sample < rejectionThreshold);

        return (int)((long)minInclusive + (long)(sample % range));
    }

    public double NextDouble() => (NextUInt64() >> 11) * Inverse53BitRange;

    public double NextDouble(double minInclusive, double maxExclusive)
    {
        if (!double.IsFinite(minInclusive))
        {
            throw new ArgumentOutOfRangeException(nameof(minInclusive), "The minimum must be finite.");
        }

        if (!double.IsFinite(maxExclusive))
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "The maximum must be finite.");
        }

        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "The exclusive maximum must be greater than the inclusive minimum.");
        }

        var unit = NextDouble();
        var range = maxExclusive - minInclusive;
        var result = double.IsFinite(range)
            ? minInclusive + (range * unit)
            : (minInclusive * (1.0 - unit)) + (maxExclusive * unit);

        if (result < minInclusive)
        {
            return minInclusive;
        }

        if (result >= maxExclusive)
        {
            var previous = Math.BitDecrement(maxExclusive);
            return previous < minInclusive ? minInclusive : previous;
        }

        return result;
    }

    public float NextFloat() => (NextUInt64() >> 40) * Inverse24BitRange;

    public double NextGaussian(double mean = 0.0, double stdDev = 1.0)
    {
        if (!double.IsFinite(mean))
        {
            throw new ArgumentOutOfRangeException(nameof(mean), "The mean must be finite.");
        }

        if (!double.IsFinite(stdDev) || stdDev < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(stdDev), "The standard deviation must be finite and non-negative.");
        }

        if (stdDev == 0.0)
        {
            return mean;
        }

        double standardNormal;
        if (_hasGaussianSample)
        {
            standardNormal = _gaussianSample;
            _hasGaussianSample = false;
        }
        else
        {
            double first;
            do
            {
                first = NextDouble();
            }
            while (first == 0.0);

            var second = NextDouble();
            var radius = Math.Sqrt(-2.0 * Math.Log(first));
            var angle = 2.0 * Math.PI * second;
            standardNormal = radius * Math.Cos(angle);
            _gaussianSample = radius * Math.Sin(angle);
            _hasGaussianSample = true;
        }

        return mean + (stdDev * standardNormal);
    }

    /// <summary>Creates a repeatable child stream without advancing this generator.</summary>
    public GfxRandom Fork(string scopeKey)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        return new GfxRandom(SeedMixer.DeriveForkSeed(_seed, scopeKey));
    }

    IGfxRandom IGfxRandom.Fork(string scopeKey) => Fork(scopeKey);

    private static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> (64 - count));
}
