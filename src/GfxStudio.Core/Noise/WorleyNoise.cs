using GfxStudio.Core.Randomness;
using GfxStudio.Core.Randomness.Seeding;

namespace GfxStudio.Core.Noise;

public enum WorleyDistanceMetric
{
    Euclidean,
    Manhattan,
    Chebyshev
}

public enum WorleyFeature
{
    F1,
    F2,
    F2MinusF1,
    CellValue
}

/// <summary>
/// Deterministic 2D/3D Cellular (Worley / Voronoi) noise generator seeded via <see cref="IGfxRandom"/>.
/// </summary>
public sealed class WorleyNoise
{
    private readonly ulong _seed;

    public WorleyNoise(IGfxRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _seed = random.NextUInt64();
    }

    public float Sample2D(
        double x,
        double y,
        double jitter = 1.0,
        WorleyDistanceMetric metric = WorleyDistanceMetric.Euclidean,
        WorleyFeature feature = WorleyFeature.F1)
    {
        var clampedJitter = Math.Clamp(jitter, 0.0, 1.0);
        var cellX = FastFloor(x);
        var cellY = FastFloor(y);

        var f1 = double.MaxValue;
        var f2 = double.MaxValue;
        ulong closestCellHash = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            var ny = cellY + dy;
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = cellX + dx;
                var cellHash = HashCell(nx, ny, 0);

                var rx = ToUnitDouble(SeedMixer.Mix(cellHash ^ 0x9E3779B97F4A7C15UL));
                var ry = ToUnitDouble(SeedMixer.Mix(cellHash ^ 0xBF58476D1CE4E5B9UL));

                var px = nx + 0.5 + ((rx - 0.5) * clampedJitter);
                var py = ny + 0.5 + ((ry - 0.5) * clampedJitter);

                var dist = ComputeDistance(x - px, y - py, metric);
                if (dist < f1)
                {
                    f2 = f1;
                    f1 = dist;
                    closestCellHash = cellHash;
                }
                else if (dist < f2)
                {
                    f2 = dist;
                }
            }
        }

        return feature switch
        {
            WorleyFeature.F1 => (float)Math.Clamp(f1, 0.0, 1.0),
            WorleyFeature.F2 => (float)Math.Clamp(f2 * 0.7, 0.0, 1.0),
            WorleyFeature.F2MinusF1 => (float)Math.Clamp(f2 - f1, 0.0, 1.0),
            WorleyFeature.CellValue => (float)ToUnitDouble(SeedMixer.Mix(closestCellHash ^ 0x94D049BB133111EBUL)),
            _ => (float)Math.Clamp(f1, 0.0, 1.0)
        };
    }

    private ulong HashCell(int cx, int cy, int cz)
    {
        var h = _seed;
        h = SeedMixer.Mix(h ^ unchecked((ulong)(long)cx * 0x517CC1B727220A95UL));
        h = SeedMixer.Mix(h ^ unchecked((ulong)(long)cy * 0x85EBCA6B27D4EB4FUL));
        h = SeedMixer.Mix(h ^ unchecked((ulong)(long)cz * 0xC2B2AE3D27D4EB4FUL));
        return h;
    }

    private static double ComputeDistance(double dx, double dy, WorleyDistanceMetric metric) =>
        metric switch
        {
            WorleyDistanceMetric.Euclidean => Math.Sqrt((dx * dx) + (dy * dy)),
            WorleyDistanceMetric.Manhattan => Math.Abs(dx) + Math.Abs(dy),
            WorleyDistanceMetric.Chebyshev => Math.Max(Math.Abs(dx), Math.Abs(dy)),
            _ => Math.Sqrt((dx * dx) + (dy * dy))
        };

    private static double ToUnitDouble(ulong value) =>
        (value >> 11) * (1.0 / (1UL << 53));

    private static int FastFloor(double x) => x >= 0 ? (int)x : (int)x - 1;
}
