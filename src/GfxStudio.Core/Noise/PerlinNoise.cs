using GfxStudio.Core.Randomness;

namespace GfxStudio.Core.Noise;

/// <summary>
/// Deterministic 2D, 3D, and 4D Perlin gradient noise generator seeded via <see cref="IGfxRandom"/>.
/// </summary>
public sealed class PerlinNoise
{
    private readonly byte[] _perm = new byte[512];

    public PerlinNoise(IGfxRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var p = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            p[i] = (byte)i;
        }

        // Deterministic Fisher-Yates shuffle using the layer's IGfxRandom stream
        for (var i = 255; i > 0; i--)
        {
            var swapIndex = random.NextInt(0, i + 1);
            (p[i], p[swapIndex]) = (p[swapIndex], p[i]);
        }

        for (var i = 0; i < 512; i++)
        {
            _perm[i] = p[i & 255];
        }
    }

    /// <summary>Evaluates 2D Perlin noise at <c>(x, y)</c>, returning a value in approximately <c>[-1, 1]</c>.</summary>
    public double Sample2D(double x, double y) => Sample3D(x, y, 0.0);

    /// <summary>Evaluates 3D Perlin noise at <c>(x, y, z)</c>, returning a value in approximately <c>[-1, 1]</c>.</summary>
    public double Sample3D(double x, double y, double z)
    {
        var xi = FastFloor(x) & 255;
        var yi = FastFloor(y) & 255;
        var zi = FastFloor(z) & 255;

        var xf = x - Math.Floor(x);
        var yf = y - Math.Floor(y);
        var zf = z - Math.Floor(z);

        var u = Fade(xf);
        var v = Fade(yf);
        var w = Fade(zf);

        var a = _perm[xi] + yi;
        var aa = _perm[a] + zi;
        var ab = _perm[a + 1] + zi;
        var b = _perm[xi + 1] + yi;
        var ba = _perm[b] + zi;
        var bb = _perm[b + 1] + zi;

        var x1 = Lerp(Grad3D(_perm[aa], xf, yf, zf), Grad3D(_perm[ba], xf - 1, yf, zf), u);
        var x2 = Lerp(Grad3D(_perm[ab], xf, yf - 1, zf), Grad3D(_perm[bb], xf - 1, yf - 1, zf), u);
        var y1 = Lerp(x1, x2, v);

        var x3 = Lerp(Grad3D(_perm[aa + 1], xf, yf, zf - 1), Grad3D(_perm[ba + 1], xf - 1, yf, zf - 1), u);
        var x4 = Lerp(Grad3D(_perm[ab + 1], xf, yf - 1, zf - 1), Grad3D(_perm[bb + 1], xf - 1, yf - 1, zf - 1), u);
        var y2 = Lerp(x3, x4, v);

        return Math.Clamp(Lerp(y1, y2, w), -1.0, 1.0);
    }

    /// <summary>Evaluates 4D Perlin noise at <c>(x, y, z, w)</c>, returning a value in approximately <c>[-1, 1]</c>.</summary>
    public double Sample4D(double x, double y, double z, double w)
    {
        var xi = FastFloor(x) & 255;
        var yi = FastFloor(y) & 255;
        var zi = FastFloor(z) & 255;
        var wi = FastFloor(w) & 255;

        var xf = x - Math.Floor(x);
        var yf = y - Math.Floor(y);
        var zf = z - Math.Floor(z);
        var wf = w - Math.Floor(w);

        var u = Fade(xf);
        var v = Fade(yf);
        var t = Fade(zf);
        var s = Fade(wf);

        var a = _perm[xi] + yi;
        var aa = _perm[a] + zi;
        var ab = _perm[a + 1] + zi;
        var b = _perm[xi + 1] + yi;
        var ba = _perm[b] + zi;
        var bb = _perm[b + 1] + zi;

        var aaa = _perm[aa] + wi;
        var aab = _perm[aa + 1] + wi;
        var aba = _perm[ab] + wi;
        var abb = _perm[ab + 1] + wi;
        var baa = _perm[ba] + wi;
        var bab = _perm[ba + 1] + wi;
        var bba = _perm[bb] + wi;
        var bbb = _perm[bb + 1] + wi;

        // w = 0 hyper-slice
        var z0 = Lerp(
            Lerp(
                Lerp(Grad4D(_perm[aaa], xf, yf, zf, wf), Grad4D(_perm[baa], xf - 1, yf, zf, wf), u),
                Lerp(Grad4D(_perm[aba], xf, yf - 1, zf, wf), Grad4D(_perm[bba], xf - 1, yf - 1, zf, wf), u),
                v),
            Lerp(
                Lerp(Grad4D(_perm[aab], xf, yf, zf - 1, wf), Grad4D(_perm[bab], xf - 1, yf, zf - 1, wf), u),
                Lerp(Grad4D(_perm[abb], xf, yf - 1, zf - 1, wf), Grad4D(_perm[bbb], xf - 1, yf - 1, zf - 1, wf), u),
                v),
            t);

        // w = 1 hyper-slice
        var z1 = Lerp(
            Lerp(
                Lerp(Grad4D(_perm[aaa + 1], xf, yf, zf, wf - 1), Grad4D(_perm[baa + 1], xf - 1, yf, zf, wf - 1), u),
                Lerp(Grad4D(_perm[aba + 1], xf, yf - 1, zf, wf - 1), Grad4D(_perm[bba + 1], xf - 1, yf - 1, zf, wf - 1), u),
                v),
            Lerp(
                Lerp(Grad4D(_perm[aab + 1], xf, yf, zf - 1, wf - 1), Grad4D(_perm[bab + 1], xf - 1, yf, zf - 1, wf - 1), u),
                Lerp(Grad4D(_perm[abb + 1], xf, yf - 1, zf - 1, wf - 1), Grad4D(_perm[bbb + 1], xf - 1, yf - 1, zf - 1, wf - 1), u),
                v),
            t);

        return Math.Clamp(Lerp(z0, z1, s), -1.0, 1.0);
    }

    private static int FastFloor(double x) => x >= 0 ? (int)x : (int)x - 1;

    private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    private static double Lerp(double a, double b, double t) => a + t * (b - a);

    private static double Grad3D(int hash, double x, double y, double z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h is 12 or 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static double Grad4D(int hash, double x, double y, double z, double w)
    {
        var h = hash & 31;
        var a = y;
        var b = z;
        var c = w;
        switch (h >> 3)
        {
            case 1:
                a = w;
                b = x;
                c = y;
                break;
            case 2:
                a = z;
                b = w;
                c = x;
                break;
            case 3:
                a = x;
                b = y;
                c = z;
                break;
        }

        return (((h & 4) == 0 ? -a : a)
            + ((h & 2) == 0 ? -b : b)
            + ((h & 1) == 0 ? -c : c)) * 0.5773502691896258;
    }
}
