using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Noise;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Effects;

/// <summary>
/// Applies spatial domain warping or layer-driven displacement (<c>warp</c>, <c>displace</c>) to a bitmap.
/// Supports both procedural multi-octave Perlin noise warping and displacement maps from another layer (<c>map = layer_id</c>).
/// </summary>
public sealed class WarpEffect(string effectType = "warp") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public IReadOnlyCollection<string> SupportedProperties =>
    [
        "strength",
        "amount",
        "scale_x",
        "scale_y",
        "wrap",
        "map",
        "displacement_map",
        "scale",
        "frequency",
        "octaves",
        "phase"
    ];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var strength = EffectPropertyHelpers.ResolveDouble(context, "strength", "amount", defaultValue: 8.0);
        var scaleX = EffectPropertyHelpers.ResolveDouble(context, "scale_x", null, defaultValue: strength);
        var scaleY = EffectPropertyHelpers.ResolveDouble(context, "scale_y", null, defaultValue: strength);
        var wrap = EffectPropertyHelpers.ResolveBool(context, "wrap", defaultValue: false);

        var mapLayerId = EffectPropertyHelpers.ResolveString(context, "map", "displacement_map", defaultValue: string.Empty);
        SKBitmap? mapBitmap = null;
        if (!string.IsNullOrWhiteSpace(mapLayerId))
        {
            mapBitmap = context.ResolveLayerBitmap?.Invoke(mapLayerId);
            if (mapBitmap is null)
            {
                context.AddDiagnostic("FX010", $"Displacement map layer '{mapLayerId}' referenced by '{context.LayerId}' could not be resolved.");
                return;
            }
        }

        var width = target.Width;
        var height = target.Height;
        using var source = target.Copy();

        if (mapBitmap is not null)
        {
            for (var y = 0; y < height; y++)
            {
                var my = Math.Clamp(y * mapBitmap.Height / Math.Max(1, height), 0, mapBitmap.Height - 1);
                for (var x = 0; x < width; x++)
                {
                    var mx = Math.Clamp(x * mapBitmap.Width / Math.Max(1, width), 0, mapBitmap.Width - 1);
                    var mapPx = mapBitmap.GetPixel(mx, my);
                    // Use Red for X displacement and Green for Y displacement (centered at 128)
                    var dx = ((mapPx.Red / 255.0) - 0.5) * 2.0 * scaleX;
                    var dy = ((mapPx.Green / 255.0) - 0.5) * 2.0 * scaleY;
                    target.SetPixel(x, y, SampleBilinear(source, x + dx, y + dy, wrap));
                }
            }

            return;
        }

        // Procedural fBm Noise Warp
        var scale = Math.Max(0.1, EffectPropertyHelpers.ResolveDouble(context, "scale", "frequency", defaultValue: 4.0));
        var octaves = Math.Clamp(EffectPropertyHelpers.ResolveInt(context, "octaves", null, defaultValue: 3), 1, 8);
        var phase = EffectPropertyHelpers.ResolveDouble(context, "phase", null, defaultValue: 0.0);

        var noiseX = new PerlinNoise(context.Random);
        var noiseY = new PerlinNoise(context.Random);
        var fbmX = new FractalBrownianMotion(noiseX, octaves, 0.5, 2.0);
        var fbmY = new FractalBrownianMotion(noiseY, octaves, 0.5, 2.0);

        var invW = width > 0 ? scale / width : 0.0;
        var invH = height > 0 ? scale / height : 0.0;

        for (var y = 0; y < height; y++)
        {
            var ny = y * invH;
            for (var x = 0; x < width; x++)
            {
                var nx = x * invW;
                var dx = ((fbmX.Sample3D(nx, ny, phase) * 2.0) - 1.0) * scaleX;
                var dy = ((fbmY.Sample3D(nx + 7.3, ny + 2.9, phase) * 2.0) - 1.0) * scaleY;
                target.SetPixel(x, y, SampleBilinear(source, x + dx, y + dy, wrap));
            }
        }
    }

    internal static SKColor SampleBilinear(SKBitmap bmp, double sx, double sy, bool wrap)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        if (w <= 0 || h <= 0) return SKColors.Transparent;

        if (!wrap && (sx < -0.5 || sx >= w - 0.5 || sy < -0.5 || sy >= h - 0.5))
        {
            var cx = Math.Clamp((int)Math.Round(sx), 0, w - 1);
            var cy = Math.Clamp((int)Math.Round(sy), 0, h - 1);
            return bmp.GetPixel(cx, cy);
        }

        var x0 = (int)Math.Floor(sx);
        var y0 = (int)Math.Floor(sy);
        var tx = sx - x0;
        var ty = sy - y0;

        int ix0, ix1, iy0, iy1;
        if (wrap)
        {
            ix0 = ((x0 % w) + w) % w;
            ix1 = (ix0 + 1) % w;
            iy0 = ((y0 % h) + h) % h;
            iy1 = (iy0 + 1) % h;
        }
        else
        {
            ix0 = Math.Clamp(x0, 0, w - 1);
            ix1 = Math.Clamp(x0 + 1, 0, w - 1);
            iy0 = Math.Clamp(y0, 0, h - 1);
            iy1 = Math.Clamp(y0 + 1, 0, h - 1);
        }

        var c00 = bmp.GetPixel(ix0, iy0);
        var c10 = bmp.GetPixel(ix1, iy0);
        var c01 = bmp.GetPixel(ix0, iy1);
        var c11 = bmp.GetPixel(ix1, iy1);

        var r = Lerp2D(c00.Red, c10.Red, c01.Red, c11.Red, tx, ty);
        var g = Lerp2D(c00.Green, c10.Green, c01.Green, c11.Green, tx, ty);
        var b = Lerp2D(c00.Blue, c10.Blue, c01.Blue, c11.Blue, tx, ty);
        var a = Lerp2D(c00.Alpha, c10.Alpha, c01.Alpha, c11.Alpha, tx, ty);
        return new SKColor(r, g, b, a);
    }

    private static byte Lerp2D(byte v00, byte v10, byte v01, byte v11, double tx, double ty)
    {
        var top = v00 + ((v10 - v00) * tx);
        var bot = v01 + ((v11 - v01) * tx);
        return (byte)Math.Clamp(Math.Round(top + ((bot - top) * ty)), 0, 255);
    }
}

/// <summary>
/// Rotates pixels around a center point with smooth radial falloff (<c>twirl</c>, <c>swirl</c>).
/// </summary>
public sealed class TwirlEffect(string effectType = "twirl") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public IReadOnlyCollection<string> SupportedProperties => ["center", "radius", "angle", "degrees"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var width = target.Width;
        var height = target.Height;
        var center = EffectPropertyHelpers.ResolvePoint(context, "center", new Point2D(width * 0.5f, height * 0.5f));
        var defaultRadius = Math.Min(width, height) * 0.5;
        var radius = Math.Max(1.0, EffectPropertyHelpers.ResolveDouble(context, "radius", null, defaultValue: defaultRadius));
        var angleDeg = EffectPropertyHelpers.ResolveDouble(context, "angle", "degrees", defaultValue: 180.0);
        var maxAngleRad = angleDeg * (Math.PI / 180.0);

        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            var dy = y - center.Y;
            for (var x = 0; x < width; x++)
            {
                var dx = x - center.X;
                var dist = Math.Sqrt((dx * dx) + (dy * dy));
                if (dist < radius)
                {
                    var factor = 1.0 - (dist / radius);
                    var theta = factor * factor * maxAngleRad;
                    var cosT = Math.Cos(-theta);
                    var sinT = Math.Sin(-theta);
                    var sx = center.X + (dx * cosT) - (dy * sinT);
                    var sy = center.Y + (dx * sinT) + (dy * cosT);
                    target.SetPixel(x, y, WarpEffect.SampleBilinear(source, sx, sy, wrap: false));
                }
            }
        }
    }
}

/// <summary>
/// Applies concentric radial or directional sinusoidal wave ripples (<c>ripple</c>) to a bitmap.
/// </summary>
public sealed class RippleEffect : ILayerEffect
{
    public string EffectType => "ripple";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["amplitude", "strength", "wavelength", "frequency", "phase", "style", "mode", "center", "wrap"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var width = target.Width;
        var height = target.Height;
        var amplitude = EffectPropertyHelpers.ResolveDouble(context, "amplitude", "strength", defaultValue: 5.0);
        var wavelength = Math.Max(1.0, EffectPropertyHelpers.ResolveDouble(context, "wavelength", "frequency", defaultValue: 16.0));
        var phase = EffectPropertyHelpers.ResolveDouble(context, "phase", null, defaultValue: 0.0);
        var style = EffectPropertyHelpers.ResolveString(context, "style", "mode", defaultValue: "radial").ToLowerInvariant();
        var center = EffectPropertyHelpers.ResolvePoint(context, "center", new Point2D(width * 0.5f, height * 0.5f));
        var wrap = EffectPropertyHelpers.ResolveBool(context, "wrap", defaultValue: false);

        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            var dy = y - center.Y;
            for (var x = 0; x < width; x++)
            {
                var dx = x - center.X;
                double sx = x, sy = y;

                if (style == "horizontal")
                {
                    sx += Math.Sin(((y / wavelength) + phase) * Math.Tau) * amplitude;
                }
                else if (style == "vertical")
                {
                    sy += Math.Sin(((x / wavelength) + phase) * Math.Tau) * amplitude;
                }
                else
                {
                    var dist = Math.Sqrt((dx * dx) + (dy * dy));
                    if (dist > 1e-4)
                    {
                        var offset = Math.Sin(((dist / wavelength) - phase) * Math.Tau) * amplitude;
                        sx += (dx / dist) * offset;
                        sy += (dy / dist) * offset;
                    }
                }

                target.SetPixel(x, y, WarpEffect.SampleBilinear(source, sx, sy, wrap));
            }
        }
    }
}

/// <summary>
/// Applies spherical lens barrel or pincushion distortion (<c>spherize</c>, <c>pinch</c>) around a center point.
/// </summary>
public sealed class SpherizeEffect(string effectType = "spherize", double defaultStrength = 0.6) : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public IReadOnlyCollection<string> SupportedProperties => ["center", "radius", "strength", "amount"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var width = target.Width;
        var height = target.Height;
        var center = EffectPropertyHelpers.ResolvePoint(context, "center", new Point2D(width * 0.5f, height * 0.5f));
        var defaultRadius = Math.Min(width, height) * 0.5;
        var radius = Math.Max(1.0, EffectPropertyHelpers.ResolveDouble(context, "radius", null, defaultValue: defaultRadius));
        var strength = Math.Clamp(EffectPropertyHelpers.ResolveDouble(context, "strength", "amount", defaultValue: defaultStrength), -1.0, 1.0);

        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            var dy = y - center.Y;
            for (var x = 0; x < width; x++)
            {
                var dx = x - center.X;
                var dist = Math.Sqrt((dx * dx) + (dy * dy));
                if (dist > 1e-4 && dist < radius)
                {
                    var rNorm = dist / radius;
                    double newR;
                    if (strength >= 0.0)
                    {
                        // Spherize (magnify center)
                        var spherical = Math.Asin(Math.Clamp(rNorm, -1.0, 1.0)) / (Math.PI * 0.5);
                        newR = (rNorm * (1.0 - strength)) + (spherical * strength);
                    }
                    else
                    {
                        // Pinch (compress center)
                        var p = -strength;
                        var pinched = Math.Pow(rNorm, 1.0 + (p * 1.5));
                        newR = (rNorm * (1.0 - p)) + (pinched * p);
                    }

                    var scale = (newR * radius) / dist;
                    var sx = center.X + (dx * scale);
                    var sy = center.Y + (dy * scale);
                    target.SetPixel(x, y, WarpEffect.SampleBilinear(source, sx, sy, wrap: false));
                }
            }
        }
    }
}

/// <summary>
/// Applies N-fold symmetrical angular reflection (<c>kaleidoscope</c>) around a center point.
/// </summary>
public sealed class KaleidoscopeEffect : ILayerEffect
{
    public string EffectType => "kaleidoscope";

    public IReadOnlyCollection<string> SupportedProperties => ["segments", "mirrors", "angle", "rotation", "center"];

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var width = target.Width;
        var height = target.Height;
        var segments = Math.Clamp(EffectPropertyHelpers.ResolveInt(context, "segments", "mirrors", defaultValue: 6), 2, 64);
        var angleDeg = EffectPropertyHelpers.ResolveDouble(context, "angle", "rotation", defaultValue: 0.0);
        var center = EffectPropertyHelpers.ResolvePoint(context, "center", new Point2D(width * 0.5f, height * 0.5f));

        var baseAngle = angleDeg * (Math.PI / 180.0);
        var sectorAngle = Math.Tau / segments;
        var halfSector = sectorAngle * 0.5;

        using var source = target.Copy();

        for (var y = 0; y < height; y++)
        {
            var dy = y - center.Y;
            for (var x = 0; x < width; x++)
            {
                var dx = x - center.X;
                var r = Math.Sqrt((dx * dx) + (dy * dy));
                var theta = Math.Atan2(dy, dx) - baseAngle;

                // Wrap theta into [0, sectorAngle) and fold around halfSector for mirror symmetry
                theta -= sectorAngle * Math.Floor(theta / sectorAngle);
                if (theta > halfSector)
                {
                    theta = sectorAngle - theta;
                }

                var finalTheta = theta + baseAngle;
                var sx = center.X + (r * Math.Cos(finalTheta));
                var sy = center.Y + (r * Math.Sin(finalTheta));
                target.SetPixel(x, y, WarpEffect.SampleBilinear(source, sx, sy, wrap: true));
            }
        }
    }
}
