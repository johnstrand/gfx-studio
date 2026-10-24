namespace GfxStudio.Core.Randomness;

/// <summary>A deterministic source of random values for .gfx evaluation.</summary>
public interface IGfxRandom
{
    ulong NextUInt64();

    int NextInt(int minInclusive, int maxExclusive);

    double NextDouble();

    double NextDouble(double minInclusive, double maxExclusive);

    float NextFloat();

    double NextGaussian(double mean = 0.0, double stdDev = 1.0);

    IGfxRandom Fork(string scopeKey);
}
