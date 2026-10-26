using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering;

/// <summary>
/// Deterministic cross-platform RGBA pixel compositor implementing W3C Compositing and Blending Level 1 modes.
/// </summary>
public static class PixelBlender
{
    /// <summary>
    /// Composites <paramref name="source"/> onto <paramref name="destination"/> at the specified pixel offset
    /// using <paramref name="blendMode"/> and <paramref name="opacity"/>.
    /// </summary>
    public static void Composite(
        SKBitmap destination,
        SKBitmap source,
        Point2D offset,
        BlendMode blendMode,
        float opacity = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);

        var clampedOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
        if (clampedOpacity <= 0.0f)
        {
            return;
        }

        var offsetX = (int)Math.Round(offset.X);
        var offsetY = (int)Math.Round(offset.Y);

        var startX = Math.Max(0, -offsetX);
        var startY = Math.Max(0, -offsetY);
        var endX = Math.Min(source.Width, destination.Width - offsetX);
        var endY = Math.Min(source.Height, destination.Height - offsetY);

        if (startX >= endX || startY >= endY)
        {
            return;
        }

        for (var sy = startY; sy < endY; sy++)
        {
            var dy = sy + offsetY;
            for (var sx = startX; sx < endX; sx++)
            {
                var srcColor = source.GetPixel(sx, sy);
                if (srcColor.Alpha == 0)
                {
                    continue;
                }

                var dx = sx + offsetX;
                var dstColor = destination.GetPixel(dx, dy);

                var blended = BlendPixel(
                    new ColorRgba(dstColor.Red, dstColor.Green, dstColor.Blue, dstColor.Alpha),
                    new ColorRgba(srcColor.Red, srcColor.Green, srcColor.Blue, srcColor.Alpha),
                    blendMode,
                    clampedOpacity);

                destination.SetPixel(dx, dy, blended.ToSKColor());
            }
        }
    }

    /// <summary>Blends a single source RGBA pixel over a backdrop RGBA pixel.</summary>
    public static ColorRgba BlendPixel(
        ColorRgba backdrop,
        ColorRgba source,
        BlendMode blendMode,
        float opacity = 1.0f)
    {
        var alphaS = (source.A / 255.0) * Math.Clamp(opacity, 0.0f, 1.0f);
        if (alphaS <= 0.0)
        {
            return backdrop;
        }

        var alphaB = backdrop.A / 255.0;
        var alphaO = alphaS + alphaB * (1.0 - alphaS);
        if (alphaO <= 0.0)
        {
            return new ColorRgba(0, 0, 0, 0);
        }

        var dr = backdrop.R / 255.0;
        var dg = backdrop.G / 255.0;
        var db = backdrop.B / 255.0;

        var sr = source.R / 255.0;
        var sg = source.G / 255.0;
        var sb = source.B / 255.0;

        var br = ApplyBlendChannel(dr, sr, blendMode);
        var bg = ApplyBlendChannel(dg, sg, blendMode);
        var bb = ApplyBlendChannel(db, sb, blendMode);

        var outR = (alphaS * (1.0 - alphaB) * sr + alphaS * alphaB * br + (1.0 - alphaS) * alphaB * dr) / alphaO;
        var outG = (alphaS * (1.0 - alphaB) * sg + alphaS * alphaB * bg + (1.0 - alphaS) * alphaB * dg) / alphaO;
        var outB = (alphaS * (1.0 - alphaB) * sb + alphaS * alphaB * bb + (1.0 - alphaS) * alphaB * db) / alphaO;

        return new ColorRgba(
            ToByte(outR),
            ToByte(outG),
            ToByte(outB),
            ToByte(alphaO));
    }

    private static double ApplyBlendChannel(double d, double s, BlendMode mode) => mode switch
    {
        BlendMode.Normal => s,
        BlendMode.Multiply => d * s,
        BlendMode.Screen => d + s - d * s,
        BlendMode.Overlay => d < 0.5 ? 2.0 * d * s : 1.0 - 2.0 * (1.0 - d) * (1.0 - s),
        BlendMode.Add => Math.Min(1.0, d + s),
        BlendMode.Subtract => Math.Max(0.0, d - s),
        BlendMode.Darken => Math.Min(d, s),
        BlendMode.Lighten => Math.Max(d, s),
        BlendMode.Difference => Math.Abs(d - s),
        _ => s
    };

    private static byte ToByte(double normalized) =>
        (byte)Math.Clamp((int)Math.Round(normalized * 255.0, MidpointRounding.AwayFromZero), 0, 255);
}
