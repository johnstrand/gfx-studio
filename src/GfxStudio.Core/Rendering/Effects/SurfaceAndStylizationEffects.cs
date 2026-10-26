using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Effects;

/// <summary>
/// Applies a directional relief emboss filter (<c>emboss</c>) to a bitmap.
/// </summary>
public sealed class EmbossEffect : ILayerEffect
{
    public string EffectType => "emboss";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["angle", "direction", "strength", "depth", "blend_original", "gray_level"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var angleDeg = EffectPropertyHelpers.ResolveDouble(context, "angle", "direction", defaultValue: 135.0);
        var strength = Math.Max(0.0, EffectPropertyHelpers.ResolveDouble(context, "strength", "depth", defaultValue: 1.5));
        var blendOriginal = EffectPropertyHelpers.ResolveBool(context, "blend_original", defaultValue: false);
        var grayLevel = Math.Clamp(EffectPropertyHelpers.ResolveInt(context, "gray_level", null, defaultValue: 128), 0, 255);

        var rad = angleDeg * (Math.PI / 180.0);
        var lx = Math.Cos(rad);
        var ly = Math.Sin(rad);

        var width = target.Width;
        var height = target.Height;
        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var orig = source.GetPixel(x, y);
                if (orig.Alpha == 0) continue;

                var (gx, gy) = ComputeSobelLuminanceGradient(source, x, y, width, height);
                var dot = ((gx * lx) + (gy * ly)) * strength;

                if (blendOriginal)
                {
                    var delta = (int)Math.Round(dot * 255.0);
                    var r = (byte)Math.Clamp(orig.Red + delta, 0, 255);
                    var g = (byte)Math.Clamp(orig.Green + delta, 0, 255);
                    var b = (byte)Math.Clamp(orig.Blue + delta, 0, 255);
                    target.SetPixel(x, y, new SKColor(r, g, b, orig.Alpha));
                }
                else
                {
                    var lum = (byte)Math.Clamp(Math.Round(grayLevel + (dot * 255.0)), 0, 255);
                    target.SetPixel(x, y, new SKColor(lum, lum, lum, orig.Alpha));
                }
            }
        }
    }

    internal static (double Gx, double Gy) ComputeSobelLuminanceGradient(SKBitmap bmp, int x, int y, int w, int h)
    {
        var xm1 = Math.Max(0, x - 1);
        var xp1 = Math.Min(w - 1, x + 1);
        var ym1 = Math.Max(0, y - 1);
        var yp1 = Math.Min(h - 1, y + 1);

        var l00 = GetLuminance(bmp.GetPixel(xm1, ym1));
        var l10 = GetLuminance(bmp.GetPixel(x, ym1));
        var l20 = GetLuminance(bmp.GetPixel(xp1, ym1));
        var l01 = GetLuminance(bmp.GetPixel(xm1, y));
        var l21 = GetLuminance(bmp.GetPixel(xp1, y));
        var l02 = GetLuminance(bmp.GetPixel(xm1, yp1));
        var l12 = GetLuminance(bmp.GetPixel(x, yp1));
        var l22 = GetLuminance(bmp.GetPixel(xp1, yp1));

        var gx = (l20 + (2.0 * l21) + l22) - (l00 + (2.0 * l01) + l02);
        var gy = (l02 + (2.0 * l12) + l22) - (l00 + (2.0 * l10) + l20);
        return (gx * 0.25, gy * 0.25);
    }

    internal static double GetLuminance(SKColor c) =>
        ((0.299 * c.Red) + (0.587 * c.Green) + (0.114 * c.Blue)) * (c.Alpha / 255.0) / 255.0;
}

/// <summary>
/// Applies inner bevel / chisel highlight and shadow lighting (<c>bevel</c>) along alpha and luminance contours.
/// </summary>
public sealed class BevelEffect : ILayerEffect
{
    public string EffectType => "bevel";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["width", "depth", "angle", "direction", "strength", "intensity", "highlight_color", "shadow_color"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var bevelWidth = Math.Clamp(EffectPropertyHelpers.ResolveInt(context, "width", "depth", defaultValue: 3), 1, 32);
        var angleDeg = EffectPropertyHelpers.ResolveDouble(context, "angle", "direction", defaultValue: 135.0);
        var strength = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "strength", "intensity", defaultValue: 0.65), 0.0, 2.0);
        var highlight = EffectPropertyHelpers.ResolveColor(context, "highlight_color", ColorRgba.White);
        var shadow = EffectPropertyHelpers.ResolveColor(context, "shadow_color", ColorRgba.Black);

        var rad = angleDeg * (Math.PI / 180.0);
        var dx = (int)Math.Round(Math.Cos(rad) * bevelWidth);
        var dy = (int)Math.Round(Math.Sin(rad) * bevelWidth);
        if (dx == 0 && dy == 0) dx = 1;

        var width = target.Width;
        var height = target.Height;
        using var source = target.Copy();

        unsafe
        {
            var srcBase = (byte*)source.GetPixels().ToPointer();
            var dstBase = (byte*)target.GetPixels().ToPointer();
            var srcRowBytes = source.RowBytes;
            var dstRowBytes = target.RowBytes;

            Parallel.For(0, height, y =>
            {
                var srcRow = srcBase + (y * srcRowBytes);
                var dstRow = dstBase + (y * dstRowBytes);

                for (var x = 0; x < width; x++)
                {
                    var orig = srcRow + (x * 4);
                    var alpha = orig[3];
                    if (alpha == 0) continue;

                    var hPlus = GetHeightSample(srcBase, srcRowBytes, x - dx, y - dy, width, height);
                    var hMinus = GetHeightSample(srcBase, srcRowBytes, x + dx, y + dy, width, height);
                    var diff = (hMinus - hPlus) * strength;

                    if (Math.Abs(diff) > 1e-4)
                    {
                        var tint = diff > 0.0 ? highlight : shadow;
                        var t = Math.Clamp(Math.Abs(diff), 0.0, 1.0) * (tint.A / 255.0);
                        var r = (byte)Math.Clamp(Math.Round(orig[0] + ((tint.R - orig[0]) * t)), 0, 255);
                        var g = (byte)Math.Clamp(Math.Round(orig[1] + ((tint.G - orig[1]) * t)), 0, 255);
                        var b = (byte)Math.Clamp(Math.Round(orig[2] + ((tint.B - orig[2]) * t)), 0, 255);

                        var dst = dstRow + (x * 4);
                        dst[0] = r;
                        dst[1] = g;
                        dst[2] = b;
                        dst[3] = alpha;
                    }
                }
            });
        }
    }

    private static unsafe double GetHeightSample(byte* srcBase, int srcRowBytes, int x, int y, int w, int h)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return 0.0;
        var px = srcBase + (y * srcRowBytes) + (x * 4);
        var alpha = px[3] / 255.0;
        var lum = ((0.299 * px[0]) + (0.587 * px[1]) + (0.114 * px[2])) / 255.0;
        return (alpha * 0.7) + (lum * alpha * 0.3);
    }
}

/// <summary>
/// Converts a bitmap's luminance heightfield into a tangent-space RGB normal map (<c>normal_map</c>).
/// </summary>
public sealed class NormalMapEffect : ILayerEffect
{
    public string EffectType => "normal_map";

    public IReadOnlyCollection<string> SupportedProperties => ["strength", "scale", "invert_x", "invert_y"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var strength = Math.Max(0.01, EffectPropertyHelpers.ResolveDouble(context, "strength", "scale", defaultValue: 3.0));
        var invertX = EffectPropertyHelpers.ResolveBool(context, "invert_x", defaultValue: false);
        var invertY = EffectPropertyHelpers.ResolveBool(context, "invert_y", defaultValue: false);

        var width = target.Width;
        var height = target.Height;
        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (gx, gy) = EmbossEffect.ComputeSobelLuminanceGradient(source, x, y, width, height);
                var nx = -gx * strength * (invertX ? -1.0 : 1.0);
                var ny = -gy * strength * (invertY ? -1.0 : 1.0);
                var nz = 1.0;

                var len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
                nx /= len;
                ny /= len;
                nz /= len;

                var r = (byte)Math.Clamp(Math.Round(((nx * 0.5) + 0.5) * 255.0), 0, 255);
                var g = (byte)Math.Clamp(Math.Round(((ny * 0.5) + 0.5) * 255.0), 0, 255);
                var b = (byte)Math.Clamp(Math.Round(((nz * 0.5) + 0.5) * 255.0), 0, 255);
                target.SetPixel(x, y, new SKColor(r, g, b, 255));
            }
        }
    }
}

/// <summary>
/// Renders a rotated dot/line/diamond halftone screen (<c>halftone</c>), supporting both monochrome and 4-screen CMYK print modes.
/// </summary>
public sealed class HalftoneEffect : ILayerEffect
{
    public string EffectType => "halftone";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["cell_size", "dot_size", "angle", "shape", "style", "color", "background", "cmyk", "invert"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var cellSize = Math.Max(2.0, EffectPropertyHelpers.ResolveDouble(context, "cell_size", "dot_size", defaultValue: 6.0));
        var angleDeg = EffectPropertyHelpers.ResolveDouble(context, "angle", null, defaultValue: 45.0);
        var shape = EffectPropertyHelpers.ResolveString(context, "shape", "style", defaultValue: "circle").ToLowerInvariant();
        var fgColor = EffectPropertyHelpers.ResolveColor(context, "color", ColorRgba.Black).ToSKColor();
        var bgColor = EffectPropertyHelpers.ResolveColor(context, "background", ColorRgba.White).ToSKColor();
        var cmyk = EffectPropertyHelpers.ResolveBool(context, "cmyk", defaultValue: false);
        var invert = EffectPropertyHelpers.ResolveBool(context, "invert", defaultValue: false);

        var width = target.Width;
        var height = target.Height;
        using var source = target.Copy();

        if (cmyk)
        {
            // Standard print rosette angles: C=15, M=75, Y=0, K=45
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var orig = source.GetPixel(x, y);
                    if (orig.Alpha == 0) continue;

                    var rNorm = orig.Red / 255.0;
                    var gNorm = orig.Green / 255.0;
                    var bNorm = orig.Blue / 255.0;
                    var k = 1.0 - Math.Max(rNorm, Math.Max(gNorm, bNorm));
                    var invK = k < 0.999 ? 1.0 / (1.0 - k) : 0.0;
                    var c = (1.0 - rNorm - k) * invK;
                    var m = (1.0 - gNorm - k) * invK;
                    var yel = (1.0 - bNorm - k) * invK;

                    var cHit = SampleScreen(x, y, cellSize, 15.0, c, shape);
                    var mHit = SampleScreen(x, y, cellSize, 75.0, m, shape);
                    var yHit = SampleScreen(x, y, cellSize, 0.0, yel, shape);
                    var kHit = SampleScreen(x, y, cellSize, 45.0, k * 0.85, shape);

                    var outR = (byte)Math.Clamp(Math.Round(255.0 * (1.0 - (cHit * 0.85)) * (1.0 - kHit)), 0, 255);
                    var outG = (byte)Math.Clamp(Math.Round(255.0 * (1.0 - (mHit * 0.85)) * (1.0 - kHit)), 0, 255);
                    var outB = (byte)Math.Clamp(Math.Round(255.0 * (1.0 - (yHit * 0.85)) * (1.0 - kHit)), 0, 255);
                    target.SetPixel(x, y, new SKColor(outR, outG, outB, orig.Alpha));
                }
            }

            return;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var orig = source.GetPixel(x, y);
                if (orig.Alpha == 0) continue;

                var lum = EmbossEffect.GetLuminance(orig);
                var darkness = invert ? lum : (1.0 - lum);
                var hit = SampleScreen(x, y, cellSize, angleDeg, darkness, shape);

                var outR = (byte)Math.Round(bgColor.Red + ((fgColor.Red - bgColor.Red) * hit));
                var outG = (byte)Math.Round(bgColor.Green + ((fgColor.Green - bgColor.Green) * hit));
                var outB = (byte)Math.Round(bgColor.Blue + ((fgColor.Blue - bgColor.Blue) * hit));
                target.SetPixel(x, y, new SKColor(outR, outG, outB, orig.Alpha));
            }
        }
    }

    private static double SampleScreen(int x, int y, double cellSize, double angleDeg, double coverage, string shape)
    {
        if (coverage <= 0.001) return 0.0;
        if (coverage >= 0.999) return 1.0;

        var rad = angleDeg * (Math.PI / 180.0);
        var cosA = Math.Cos(rad);
        var sinA = Math.Sin(rad);
        var u = ((x + 0.5) * cosA) - ((y + 0.5) * sinA);
        var v = ((x + 0.5) * sinA) + ((y + 0.5) * cosA);

        // Local coordinates in [-0.5, 0.5] within the rotated screen cell
        var lu = (u / cellSize) - Math.Floor(u / cellSize) - 0.5;
        var lv = (v / cellSize) - Math.Floor(v / cellSize) - 0.5;

        double dist;
        double threshold;
        switch (shape)
        {
            case "line" or "lines":
                dist = Math.Abs(lv);
                threshold = coverage * 0.5;
                break;

            case "diamond":
                dist = (Math.Abs(lu) + Math.Abs(lv)) * 0.7071;
                threshold = Math.Sqrt(coverage) * 0.5;
                break;

            case "cross":
                dist = Math.Min(Math.Abs(lu), Math.Abs(lv));
                threshold = coverage * 0.35;
                break;

            default: // "circle"
                dist = Math.Sqrt((lu * lu) + (lv * lv));
                threshold = Math.Sqrt(coverage) * 0.56;
                break;
        }

        var edgeWidth = 0.75 / cellSize;
        return Math.Clamp((threshold + (edgeWidth * 0.5) - dist) / edgeWidth, 0.0, 1.0);
    }
}

/// <summary>
/// Applies ordered Bayer, Floyd-Steinberg, Atkinson, or stochastic stipple dithering (<c>dither</c>, <c>stipple</c>).
/// </summary>
public sealed class DitherStippleEffect(string effectType = "dither", string defaultStyle = "bayer") : ILayerEffect
{
    private static readonly double[,] Bayer4x4 =
    {
        {  0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0 },
        { 12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0 },
        {  3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0 },
        { 15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0 }
    };

    public string EffectType { get; } = effectType;

    public IReadOnlyCollection<string> SupportedProperties =>
        ["style", "mode", "levels", "scale", "cell_size", "color", "background"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var style = EffectPropertyHelpers.ResolveString(context, "style", "mode", defaultValue: defaultStyle).ToLowerInvariant();
        var levels = Math.Clamp(EffectPropertyHelpers.ResolveInt(context, "levels", null, defaultValue: 2), 2, 64);
        var scale = Math.Clamp(EffectPropertyHelpers.ResolveInt(context, "scale", "cell_size", defaultValue: 1), 1, 16);
        var hasCustomColors = context.Properties.TryGetProperty("color", out _) || context.Properties.TryGetProperty("background", out _);
        var fgColor = EffectPropertyHelpers.ResolveColor(context, "color", ColorRgba.White).ToSKColor();
        var bgColor = EffectPropertyHelpers.ResolveColor(context, "background", ColorRgba.Black).ToSKColor();

        var width = target.Width;
        var height = target.Height;

        if (style is "floyd_steinberg" or "atkinson")
        {
            ApplyErrorDiffusion(target, width, height, levels, style == "atkinson", hasCustomColors, fgColor, bgColor);
            return;
        }

        using var source = target.Copy();
        var step = 1.0 / (levels - 1);

        for (var y = 0; y < height; y++)
        {
            var sy = (y / scale) * scale;
            for (var x = 0; x < width; x++)
            {
                var sx = (x / scale) * scale;
                var orig = source.GetPixel(sx, sy);
                if (orig.Alpha == 0) continue;

                double thresholdOffset;
                if (style == "stipple")
                {
                    // Deterministic spatial hash per scaled block
                    var h = GfxStudio.Core.Randomness.Seeding.SeedMixer.Mix(
                        unchecked((ulong)(sx / scale) * 0x9E3779B97F4A7C15UL) ^
                        unchecked((ulong)(sy / scale) * 0xBF58476D1CE4E5B9UL) ^
                        0xD1B54A32D192ED03UL);
                    thresholdOffset = ((h >> 11) * (1.0 / (1UL << 53))) - 0.5;
                }
                else
                {
                    thresholdOffset = Bayer4x4[(y / scale) & 3, (x / scale) & 3] - 0.5 + (1.0 / 32.0);
                }

                if (hasCustomColors || style == "stipple")
                {
                    var lum = EmbossEffect.GetLuminance(orig);
                    var q = Math.Clamp(Math.Round((lum + (thresholdOffset * step)) * (levels - 1)) / (levels - 1), 0.0, 1.0);
                    var r = (byte)Math.Round(bgColor.Red + ((fgColor.Red - bgColor.Red) * q));
                    var g = (byte)Math.Round(bgColor.Green + ((fgColor.Green - bgColor.Green) * q));
                    var b = (byte)Math.Round(bgColor.Blue + ((fgColor.Blue - bgColor.Blue) * q));
                    target.SetPixel(x, y, new SKColor(r, g, b, orig.Alpha));
                }
                else
                {
                    var r = QuantizeChannel(orig.Red / 255.0, thresholdOffset * step, levels);
                    var g = QuantizeChannel(orig.Green / 255.0, thresholdOffset * step, levels);
                    var b = QuantizeChannel(orig.Blue / 255.0, thresholdOffset * step, levels);
                    target.SetPixel(x, y, new SKColor(r, g, b, orig.Alpha));
                }
            }
        }
    }

    private static byte QuantizeChannel(double val, double ditherOffset, int levels)
    {
        var q = Math.Clamp(Math.Round((val + ditherOffset) * (levels - 1)) / (levels - 1), 0.0, 1.0);
        return (byte)Math.Clamp(Math.Round(q * 255.0), 0, 255);
    }

    private static void ApplyErrorDiffusion(
        SKBitmap target,
        int width,
        int height,
        int levels,
        bool atkinson,
        bool duoTone,
        SKColor fg,
        SKColor bg)
    {
        var bufR = new float[width * height];
        var bufG = new float[width * height];
        var bufB = new float[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var px = target.GetPixel(x, y);
                var idx = (y * width) + x;
                bufR[idx] = px.Red / 255.0f;
                bufG[idx] = px.Green / 255.0f;
                bufB[idx] = px.Blue / 255.0f;
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var orig = target.GetPixel(x, y);
                if (orig.Alpha == 0) continue;

                var idx = (y * width) + x;
                var oldR = Math.Clamp(bufR[idx], 0f, 1f);
                var oldG = Math.Clamp(bufG[idx], 0f, 1f);
                var oldB = Math.Clamp(bufB[idx], 0f, 1f);

                var newR = (float)Math.Round(oldR * (levels - 1)) / (levels - 1);
                var newG = (float)Math.Round(oldG * (levels - 1)) / (levels - 1);
                var newB = (float)Math.Round(oldB * (levels - 1)) / (levels - 1);

                var errR = oldR - newR;
                var errG = oldG - newG;
                var errB = oldB - newB;

                if (atkinson)
                {
                    var w = 1.0f / 8.0f;
                    AddError(bufR, bufG, bufB, width, height, x + 1, y, errR, errG, errB, w);
                    AddError(bufR, bufG, bufB, width, height, x + 2, y, errR, errG, errB, w);
                    AddError(bufR, bufG, bufB, width, height, x - 1, y + 1, errR, errG, errB, w);
                    AddError(bufR, bufG, bufB, width, height, x, y + 1, errR, errG, errB, w);
                    AddError(bufR, bufG, bufB, width, height, x + 1, y + 1, errR, errG, errB, w);
                    AddError(bufR, bufG, bufB, width, height, x, y + 2, errR, errG, errB, w);
                }
                else
                {
                    AddError(bufR, bufG, bufB, width, height, x + 1, y, errR, errG, errB, 7.0f / 16.0f);
                    AddError(bufR, bufG, bufB, width, height, x - 1, y + 1, errR, errG, errB, 3.0f / 16.0f);
                    AddError(bufR, bufG, bufB, width, height, x, y + 1, errR, errG, errB, 5.0f / 16.0f);
                    AddError(bufR, bufG, bufB, width, height, x + 1, y + 1, errR, errG, errB, 1.0f / 16.0f);
                }

                if (duoTone)
                {
                    var lum = (0.299f * newR) + (0.587f * newG) + (0.114f * newB);
                    var r = (byte)Math.Round(bg.Red + ((fg.Red - bg.Red) * lum));
                    var g = (byte)Math.Round(bg.Green + ((fg.Green - bg.Green) * lum));
                    var b = (byte)Math.Round(bg.Blue + ((fg.Blue - bg.Blue) * lum));
                    target.SetPixel(x, y, new SKColor(r, g, b, orig.Alpha));
                }
                else
                {
                    target.SetPixel(x, y, new SKColor(
                        (byte)Math.Clamp(Math.Round(newR * 255.0f), 0, 255),
                        (byte)Math.Clamp(Math.Round(newG * 255.0f), 0, 255),
                        (byte)Math.Clamp(Math.Round(newB * 255.0f), 0, 255),
                        orig.Alpha));
                }
            }
        }
    }

    private static void AddError(float[] r, float[] g, float[] b, int w, int h, int x, int y, float er, float eg, float eb, float weight)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        var idx = (y * w) + x;
        r[idx] += er * weight;
        g[idx] += eg * weight;
        b[idx] += eb * weight;
    }
}

/// <summary>
/// Applies a 3x3 Sobel gradient magnitude edge detector (<c>edge_detect</c>, <c>sobel</c>).
/// </summary>
public sealed class EdgeDetectEffect(string effectType = "edge_detect") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public IReadOnlyCollection<string> SupportedProperties =>
        ["strength", "scale", "threshold", "invert", "color", "background"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var strength = Math.Max(0.1, EffectPropertyHelpers.ResolveDouble(context, "strength", "scale", defaultValue: 2.0));
        var threshold = EffectPropertyHelpers.ResolveDouble(context, "threshold", null, defaultValue: 0.0);
        var invert = EffectPropertyHelpers.ResolveBool(context, "invert", defaultValue: false);
        var edgeColor = EffectPropertyHelpers.ResolveColor(context, "color", ColorRgba.White).ToSKColor();
        var bgColor = EffectPropertyHelpers.ResolveColor(context, "background", ColorRgba.Black).ToSKColor();

        var width = target.Width;
        var height = target.Height;
        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var orig = source.GetPixel(x, y);
                var (gx, gy) = EmbossEffect.ComputeSobelLuminanceGradient(source, x, y, width, height);
                var mag = Math.Clamp(Math.Sqrt((gx * gx) + (gy * gy)) * strength, 0.0, 1.0);
                if (threshold > 0.0)
                {
                    mag = mag >= threshold ? 1.0 : 0.0;
                }

                if (invert)
                {
                    mag = 1.0 - mag;
                }

                var r = (byte)Math.Round(bgColor.Red + ((edgeColor.Red - bgColor.Red) * mag));
                var g = (byte)Math.Round(bgColor.Green + ((edgeColor.Green - bgColor.Green) * mag));
                var b = (byte)Math.Round(bgColor.Blue + ((edgeColor.Blue - bgColor.Blue) * mag));
                target.SetPixel(x, y, new SKColor(r, g, b, orig.Alpha));
            }
        }
    }
}

/// <summary>
/// Applies smooth radial edge darkening or tinting (<c>vignette</c>).
/// </summary>
public sealed class VignetteEffect : ILayerEffect
{
    public string EffectType => "vignette";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["intensity", "strength", "radius", "softness", "feather", "color"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var intensity = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "intensity", "strength", defaultValue: 0.75), 0.0, 1.0);
        var radius = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "radius", null, defaultValue: 0.75), 0.05, 2.0);
        var softness = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "softness", "feather", defaultValue: 0.45), 0.01, 2.0);
        var vigColor = EffectPropertyHelpers.ResolveColor(context, "color", ColorRgba.Black);

        var width = target.Width;
        var height = target.Height;
        var cx = width * 0.5;
        var cy = height * 0.5;
        var invRx = cx > 0 ? 1.0 / cx : 0.0;
        var invRy = cy > 0 ? 1.0 / cy : 0.0;

        unsafe
        {
            var targetBase = (byte*)target.GetPixels().ToPointer();
            var targetRowBytes = target.RowBytes;

            Parallel.For(0, height, y =>
            {
                var dy = (y + 0.5 - cy) * invRy;
                var tRow = targetBase + (y * targetRowBytes);

                for (var x = 0; x < width; x++)
                {
                    var orig = tRow + (x * 4);
                    if (orig[3] == 0) continue;

                    var dx = (x + 0.5 - cx) * invRx;
                    var dist = Math.Sqrt((dx * dx) + (dy * dy));
                    var t = Math.Clamp((dist - (radius - (softness * 0.5))) / softness, 0.0, 1.0);
                    // Smoothstep falloff
                    var smooth = t * t * (3.0 - (2.0 * t)) * intensity;

                    orig[0] = (byte)Math.Clamp(Math.Round(orig[0] + ((vigColor.R - orig[0]) * smooth)), 0, 255);
                    orig[1] = (byte)Math.Clamp(Math.Round(orig[1] + ((vigColor.G - orig[1]) * smooth)), 0, 255);
                    orig[2] = (byte)Math.Clamp(Math.Round(orig[2] + ((vigColor.B - orig[2]) * smooth)), 0, 255);
                }
            });
        }
    }
}

/// <summary>
/// Extracts bright highlights above <c>threshold</c>, blurs them, and additively composites a soft halo (<c>bloom</c>, <c>glow</c>).
/// </summary>
public sealed class BloomEffect(string effectType = "bloom") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public IReadOnlyCollection<string> SupportedProperties =>
        ["threshold", "radius", "blur", "intensity", "strength"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var threshold = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "threshold", null, defaultValue: 0.6), 0.0, 1.0);
        var radius = Math.Max(0.5, EffectPropertyHelpers.ResolveDouble(context, "radius", "blur", defaultValue: 5.0));
        var intensity = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "intensity", "strength", defaultValue: 1.0), 0.0, 4.0);

        var width = target.Width;
        var height = target.Height;
        using var brightPass = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        brightPass.Erase(SKColors.Transparent);

        unsafe
        {
            var targetBase = (byte*)target.GetPixels().ToPointer();
            var targetRowBytes = target.RowBytes;
            var brightBase = (byte*)brightPass.GetPixels().ToPointer();
            var brightRowBytes = brightPass.RowBytes;

            Parallel.For(0, height, y =>
            {
                var tRow = targetBase + (y * targetRowBytes);
                var bRow = brightBase + (y * brightRowBytes);

                for (var x = 0; x < width; x++)
                {
                    var px = tRow + (x * 4);
                    var lum = ((0.299 * px[0]) + (0.587 * px[1]) + (0.114 * px[2])) * (px[3] / 255.0) / 255.0;
                    if (lum > threshold)
                    {
                        var factor = (lum - threshold) / Math.Max(1e-4, 1.0 - threshold);
                        var bp = bRow + (x * 4);
                        bp[0] = (byte)Math.Clamp(Math.Round(px[0] * factor), 0, 255);
                        bp[1] = (byte)Math.Clamp(Math.Round(px[1] * factor), 0, 255);
                        bp[2] = (byte)Math.Clamp(Math.Round(px[2] * factor), 0, 255);
                        bp[3] = px[3];
                    }
                }
            });

            using var blurred = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            blurred.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(blurred))
            using (var filter = SKImageFilter.CreateBlur((float)radius, (float)radius))
            using (var paint = new SKPaint { ImageFilter = filter, IsAntialias = true })
            {
                canvas.DrawBitmap(brightPass, 0, 0, paint);
                canvas.Flush();
            }

            var blurBase = (byte*)blurred.GetPixels().ToPointer();
            var blurRowBytes = blurred.RowBytes;

            Parallel.For(0, height, y =>
            {
                var tRow = targetBase + (y * targetRowBytes);
                var gRow = blurBase + (y * blurRowBytes);

                for (var x = 0; x < width; x++)
                {
                    var orig = tRow + (x * 4);
                    var glow = gRow + (x * 4);
                    var r = (byte)Math.Clamp(Math.Round(orig[0] + (glow[0] * intensity)), 0, 255);
                    var g = (byte)Math.Clamp(Math.Round(orig[1] + (glow[1] * intensity)), 0, 255);
                    var b = (byte)Math.Clamp(Math.Round(orig[2] + (glow[2] * intensity)), 0, 255);
                    var a = (byte)Math.Clamp(Math.Max((int)orig[3], (int)Math.Round(glow[3] * intensity)), 0, 255);
                    orig[0] = r;
                    orig[1] = g;
                    orig[2] = b;
                    orig[3] = a;
                }
            });
        }
    }
}

/// <summary>
/// Shifts Red and Blue color channels in opposite directions to simulate lens dispersion (<c>chromatic_aberration</c>).
/// </summary>
public sealed class ChromaticAberrationEffect : ILayerEffect
{
    public string EffectType => "chromatic_aberration";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["angle", "radial", "offset", "shift", "strength"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var angleDeg = EffectPropertyHelpers.ResolveDouble(context, "angle", null, defaultValue: 0.0);
        var radial = EffectPropertyHelpers.ResolveBool(context, "radial", defaultValue: false);

        double dirX, dirY, offsetMagnitude;
        if ((context.Properties.TryGetProperty("offset", out var offsetVal) || context.Properties.TryGetProperty("shift", out offsetVal))
            && offsetVal.TryCoerceToPoint2D(out var pt))
        {
            dirX = pt.X;
            dirY = pt.Y;
            offsetMagnitude = Math.Sqrt((dirX * dirX) + (dirY * dirY));
        }
        else
        {
            offsetMagnitude = EffectPropertyHelpers.ResolveDouble(context, "offset", "strength", defaultValue: 3.0);
            var rad = angleDeg * (Math.PI / 180.0);
            dirX = Math.Cos(rad) * offsetMagnitude;
            dirY = Math.Sin(rad) * offsetMagnitude;
        }

        var width = target.Width;
        var height = target.Height;
        var cx = width * 0.5;
        var cy = height * 0.5;
        var maxR = Math.Max(1.0, Math.Sqrt((cx * cx) + (cy * cy)));

        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double ox = dirX, oy = dirY;
                if (radial)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    ox = (dx / maxR) * offsetMagnitude;
                    oy = (dy / maxR) * offsetMagnitude;
                }

                var colR = WarpEffect.SampleBilinear(source, x - ox, y - oy, wrap: false);
                var colG = source.GetPixel(x, y);
                var colB = WarpEffect.SampleBilinear(source, x + ox, y + oy, wrap: false);
                var a = (byte)Math.Max(colG.Alpha, Math.Max(colR.Alpha, colB.Alpha));

                target.SetPixel(x, y, new SKColor(colR.Red, colG.Green, colB.Blue, a));
            }
        }
    }
}

/// <summary>
/// Applies retro CRT monitor horizontal scanlines and optional screen curvature (<c>crt</c>, <c>scanlines</c>).
/// </summary>
public sealed class CrtScanlinesEffect(string effectType = "crt") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public IReadOnlyCollection<string> SupportedProperties =>
        ["spacing", "period", "opacity", "strength", "curvature", "scanline_opacity"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var spacing = Math.Max(2, EffectPropertyHelpers.ResolveInt(context, "spacing", "period", defaultValue: 2));
        var opacity = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "opacity", "strength", defaultValue: 0.35), 0.0, 1.0);
        if (context.Properties.TryGetProperty("scanline_opacity", out var soVal) && soVal.TryCoerceToDouble(out var soNum))
        {
            opacity = (float)Math.Clamp(soNum, 0.0, 1.0);
        }
        var curvature = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "curvature", null, defaultValue: 0.0), 0.0, 1.0);

        var width = target.Width;
        var height = target.Height;
        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                SKColor px;
                if (curvature > 0.0)
                {
                    var nx = ((x + 0.5) / width * 2.0) - 1.0;
                    var ny = ((y + 0.5) / height * 2.0) - 1.0;
                    var r2 = (nx * nx) + (ny * ny);
                    var factor = 1.0 + (r2 * curvature * 0.25);
                    var sx = ((nx * factor) + 1.0) * 0.5 * width;
                    var sy = ((ny * factor) + 1.0) * 0.5 * height;
                    if (sx < 0 || sx >= width || sy < 0 || sy >= height)
                    {
                        target.SetPixel(x, y, new SKColor(0, 0, 0, 255));
                        continue;
                    }

                    px = WarpEffect.SampleBilinear(source, sx, sy, wrap: false);
                }
                else
                {
                    px = source.GetPixel(x, y);
                }

                if ((y % spacing) == 0)
                {
                    var keep = 1.0 - opacity;
                    px = new SKColor(
                        (byte)Math.Round(px.Red * keep),
                        (byte)Math.Round(px.Green * keep),
                        (byte)Math.Round(px.Blue * keep),
                        px.Alpha);
                }

                target.SetPixel(x, y, px);
            }
        }
    }
}

/// <summary>
/// Applies an anisotropic Kuwahara painterly smoothing filter (<c>oil_paint</c>, <c>kuwahara</c>) that preserves edges.
/// </summary>
public sealed class KuwaharaEffect(string effectType = "oil_paint") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public IReadOnlyCollection<string> SupportedProperties => ["radius", "size"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var radius = Math.Clamp(EffectPropertyHelpers.ResolveInt(context, "radius", "size", defaultValue: 3), 1, 12);
        var width = target.Width;
        var height = target.Height;
        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var orig = source.GetPixel(x, y);
                if (orig.Alpha == 0) continue;

                var bestVariance = double.MaxValue;
                var bestColor = orig;

                // Evaluate 4 overlapping quadrants around (x, y)
                for (var q = 0; q < 4; q++)
                {
                    var xMin = q is 0 or 2 ? Math.Max(0, x - radius) : x;
                    var xMax = q is 0 or 2 ? x : Math.Min(width - 1, x + radius);
                    var yMin = q is 0 or 1 ? Math.Max(0, y - radius) : y;
                    var yMax = q is 0 or 1 ? y : Math.Min(height - 1, y + radius);

                    double sumR = 0, sumG = 0, sumB = 0, sumLum = 0, sumLumSq = 0;
                    var count = 0;

                    for (var sy = yMin; sy <= yMax; sy++)
                    {
                        for (var sx = xMin; sx <= xMax; sx++)
                        {
                            var p = source.GetPixel(sx, sy);
                            var lum = ((0.299 * p.Red) + (0.587 * p.Green) + (0.114 * p.Blue)) / 255.0;
                            sumR += p.Red;
                            sumG += p.Green;
                            sumB += p.Blue;
                            sumLum += lum;
                            sumLumSq += lum * lum;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        var meanLum = sumLum / count;
                        var variance = (sumLumSq / count) - (meanLum * meanLum);
                        if (variance < bestVariance)
                        {
                            bestVariance = variance;
                            bestColor = new SKColor(
                                (byte)Math.Clamp(Math.Round(sumR / count), 0, 255),
                                (byte)Math.Clamp(Math.Round(sumG / count), 0, 255),
                                (byte)Math.Clamp(Math.Round(sumB / count), 0, 255),
                                orig.Alpha);
                        }
                    }
                }

                target.SetPixel(x, y, bestColor);
            }
        }
    }
}
