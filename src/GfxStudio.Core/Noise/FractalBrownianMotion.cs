namespace GfxStudio.Core.Noise;

/// <summary>
/// Evaluates multi-octave Fractal Brownian Motion (fBm) over a <see cref="PerlinNoise"/> source,
/// returning normalized values in <c>[0, 1]</c>.
/// </summary>
public sealed class FractalBrownianMotion
{
    public const int MinOctaves = 1;
    public const int MaxOctaves = 12;

    private readonly PerlinNoise _noise;

    public FractalBrownianMotion(
        PerlinNoise noise,
        int octaves = 5,
        double persistence = 0.5,
        double lacunarity = 2.0)
    {
        ArgumentNullException.ThrowIfNull(noise);
        _noise = noise;
        Octaves = Math.Clamp(octaves, MinOctaves, MaxOctaves);
        Persistence = persistence;
        Lacunarity = lacunarity;
    }

    public int Octaves { get; }

    public double Persistence { get; }

    public double Lacunarity { get; }

    /// <summary>Samples 3D fBm at <c>(x, y, z)</c> and normalizes the result to <c>[0.0, 1.0]</c>.</summary>
    public float Sample3D(double x, double y, double z = 0.0)
    {
        var total = 0.0;
        var amplitude = 1.0;
        var frequency = 1.0;
        var maxAmplitude = 0.0;

        for (var i = 0; i < Octaves; i++)
        {
            total += _noise.Sample3D(x * frequency, y * frequency, z * frequency) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= Persistence;
            frequency *= Lacunarity;
        }

        var normalized = maxAmplitude > 0.0 ? (total / maxAmplitude) : 0.0;
        return NormalizeToUnitRange(normalized);
    }

    /// <summary>
    /// Samples 4D fBm where <paramref name="phase"/> (<c>0..1</c>) is mapped onto a circle in the 3rd and 4th dimensions
    /// so that <c>phase = 0.0</c> and <c>phase = 1.0</c> produce identical results for seamless loops.
    /// </summary>
    public float SampleSeamlessLoop(double x, double y, double phase, double loopRadius = 1.0)
    {
        // Wrap phase modulo 1.0 so phase = 0.0 and phase = 1.0 evaluate at the exact same angle 0.0
        var wrappedPhase = phase - Math.Floor(phase);
        var angle = wrappedPhase * Math.Tau;
        var z = Math.Cos(angle) * loopRadius;
        var w = Math.Sin(angle) * loopRadius;

        var total = 0.0;
        var amplitude = 1.0;
        var frequency = 1.0;
        var maxAmplitude = 0.0;

        for (var i = 0; i < Octaves; i++)
        {
            total += _noise.Sample4D(x * frequency, y * frequency, z * frequency, w * frequency) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= Persistence;
            frequency *= Lacunarity;
        }

        var normalized = maxAmplitude > 0.0 ? (total / maxAmplitude) : 0.0;
        return NormalizeToUnitRange(normalized);
    }

    private static float NormalizeToUnitRange(double signedValue)
    {
        // Perlin gradient noise is bounded within [-1, 1]; map [-0.7, 0.7] with clamping to [0, 1] for full contrast
        var scaled = (signedValue * 0.7) + 0.5;
        return (float)Math.Clamp(scaled, 0.0, 1.0);
    }
}
