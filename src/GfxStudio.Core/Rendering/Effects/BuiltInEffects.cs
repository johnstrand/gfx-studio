using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Effects;

/// <summary>Applies a Gaussian or box blur (<c>gaussian_blur</c>, <c>box_blur</c>, <c>blur</c>) to a bitmap.</summary>
public sealed class GaussianBlurEffect(string effectType = "gaussian_blur") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var radius = EffectPropertyHelpers.ResolveDouble(context, "radius", "sigma", defaultValue: 2.0);
        if (radius < 0.0 || !double.IsFinite(radius))
        {
            context.AddDiagnostic("FX001", $"Blur radius on layer '{context.LayerId}' must be a non-negative number.");
            return;
        }

        if (radius <= 1e-5)
        {
            return;
        }

        using var copy = target.Copy();
        target.Erase(SKColors.Transparent);

        using var canvas = new SKCanvas(target);
        using var filter = SKImageFilter.CreateBlur((float)radius, (float)radius);
        using var paint = new SKPaint
        {
            ImageFilter = filter,
            IsAntialias = true
        };
        canvas.DrawBitmap(copy, 0, 0, paint);
        canvas.Flush();
    }
}

/// <summary>Applies a binary luminance cutoff (<c>threshold</c>), mapping pixels to <c>high_color</c> and <c>low_color</c>.</summary>
public sealed class ThresholdEffect : ILayerEffect
{
    public string EffectType => "threshold";

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var level = EffectPropertyHelpers.ResolveDouble(context, "level", "threshold", defaultValue: 0.5);
        if (!double.IsFinite(level) || level is < 0.0 or > 1.0)
        {
            context.AddDiagnostic("FX002", $"Threshold 'level' on layer '{context.LayerId}' must be in [0, 1].");
            level = Math.Clamp(level, 0.0, 1.0);
        }

        var highColor = EffectPropertyHelpers.ResolveColor(context, "high_color", ColorRgba.White).ToSKColor();
        var lowColor = EffectPropertyHelpers.ResolveColor(context, "low_color", ColorRgba.Black).ToSKColor();

        var width = target.Width;
        var height = target.Height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = target.GetPixel(x, y);
                var luminance = ((0.2126 * p.Red) + (0.7152 * p.Green) + (0.0722 * p.Blue)) / 255.0 * (p.Alpha / 255.0);
                target.SetPixel(x, y, luminance >= level ? highColor : lowColor);
            }
        }
    }
}

/// <summary>Quantizes each RGB channel of a bitmap to a discrete number of <c>levels</c> (<c>2..64</c>).</summary>
public sealed class PosterizeEffect : ILayerEffect
{
    public string EffectType => "posterize";

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var levels = EffectPropertyHelpers.ResolveInt(context, "levels", "steps", defaultValue: 4);
        if (levels is < 2 or > 64)
        {
            context.AddDiagnostic("FX003", $"Posterize 'levels' on layer '{context.LayerId}' must be between 2 and 64.");
            levels = Math.Clamp(levels, 2, 64);
        }

        var width = target.Width;
        var height = target.Height;
        var stepFactor = levels - 1.0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = target.GetPixel(x, y);
                if (p.Alpha == 0)
                {
                    continue;
                }

                var r = QuantizeChannel(p.Red, stepFactor);
                var g = QuantizeChannel(p.Green, stepFactor);
                var b = QuantizeChannel(p.Blue, stepFactor);
                target.SetPixel(x, y, new SKColor(r, g, b, p.Alpha));
            }
        }
    }

    private static byte QuantizeChannel(byte value, double stepFactor)
    {
        var levelIndex = Math.Round((value / 255.0) * stepFactor, MidpointRounding.AwayFromZero);
        var quantized = Math.Round((levelIndex / stepFactor) * 255.0, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp((int)quantized, 0, 255);
    }
}

/// <summary>Downsamples and nearest-neighbor upsamples a bitmap in <c>block_size</c> pixel blocks (<c>pixelate</c>).</summary>
public sealed class PixelateEffect : ILayerEffect
{
    public string EffectType => "pixelate";

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var blockSize = EffectPropertyHelpers.ResolveInt(context, "block_size", "size", defaultValue: 4);
        if (blockSize < 1)
        {
            context.AddDiagnostic("FX004", $"Pixelate 'block_size' on layer '{context.LayerId}' must be a positive integer.");
            return;
        }

        if (blockSize == 1)
        {
            return;
        }

        var width = target.Width;
        var height = target.Height;

        for (var by = 0; by < height; by += blockSize)
        {
            var maxY = Math.Min(height, by + blockSize);
            for (var bx = 0; bx < width; bx += blockSize)
            {
                var maxX = Math.Min(width, bx + blockSize);
                long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                var count = 0;

                for (var y = by; y < maxY; y++)
                {
                    for (var x = bx; x < maxX; x++)
                    {
                        var p = target.GetPixel(x, y);
                        sumR += p.Red;
                        sumG += p.Green;
                        sumB += p.Blue;
                        sumA += p.Alpha;
                        count++;
                    }
                }

                var blockColor = new SKColor(
                    (byte)Math.Round(sumR / (double)count),
                    (byte)Math.Round(sumG / (double)count),
                    (byte)Math.Round(sumB / (double)count),
                    (byte)Math.Round(sumA / (double)count));

                for (var y = by; y < maxY; y++)
                {
                    for (var x = bx; x < maxX; x++)
                    {
                        target.SetPixel(x, y, blockColor);
                    }
                }
            }
        }
    }
}

/// <summary>
/// Adjusts brightness, contrast, saturation, hue shift, and color inversion (<c>color_adjust</c>).
/// </summary>
public sealed class ColorAdjustEffect(string effectType = "color_adjust", bool defaultInvert = false) : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var brightness = EffectPropertyHelpers.ResolveDouble(context, "brightness", null, defaultValue: 0.0);
        var contrast = EffectPropertyHelpers.ResolveDouble(context, "contrast", null, defaultValue: 0.0);
        var saturation = EffectPropertyHelpers.ResolveDouble(context, "saturation", null, defaultValue: 0.0);
        var hueShift = EffectPropertyHelpers.ResolveDouble(context, "hue_shift", "hue", defaultValue: 0.0);
        var invert = EffectPropertyHelpers.ResolveBool(context, "invert", defaultValue: defaultInvert);

        var contrastFactor = Math.Pow(1.0 + Math.Clamp(contrast, -1.0, 1.0), 2.0);
        var satFactor = Math.Max(0.0, 1.0 + saturation);
        var brightnessOffset = brightness * 255.0;

        var width = target.Width;
        var height = target.Height;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = target.GetPixel(x, y);
                if (p.Alpha == 0)
                {
                    continue;
                }

                double r = p.Red;
                double g = p.Green;
                double b = p.Blue;

                if (invert)
                {
                    r = 255.0 - r;
                    g = 255.0 - g;
                    b = 255.0 - b;
                }

                // Brightness & contrast
                r = ((r - 127.5) * contrastFactor) + 127.5 + brightnessOffset;
                g = ((g - 127.5) * contrastFactor) + 127.5 + brightnessOffset;
                b = ((b - 127.5) * contrastFactor) + 127.5 + brightnessOffset;

                // Saturation
                if (Math.Abs(saturation) > 1e-6)
                {
                    var lum = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
                    r = lum + ((r - lum) * satFactor);
                    g = lum + ((g - lum) * satFactor);
                    b = lum + ((b - lum) * satFactor);
                }

                // Hue shift
                if (Math.Abs(hueShift) > 1e-6)
                {
                    (r, g, b) = RotateHue(r, g, b, hueShift);
                }

                target.SetPixel(
                    x,
                    y,
                    new SKColor(
                        (byte)Math.Clamp((int)Math.Round(r), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(g), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(b), 0, 255),
                        p.Alpha));
            }
        }
    }

    private static (double R, double G, double B) RotateHue(double r, double g, double b, double degrees)
    {
        var rad = degrees * (Math.PI / 180.0);
        var cosA = Math.Cos(rad);
        var sinA = Math.Sin(rad);
        var sqrtThird = 0.5773502691896258;

        var m00 = cosA + ((1.0 - cosA) / 3.0);
        var m01 = ((1.0 / 3.0) * (1.0 - cosA)) - (sqrtThird * sinA);
        var m02 = ((1.0 / 3.0) * (1.0 - cosA)) + (sqrtThird * sinA);

        var m10 = ((1.0 / 3.0) * (1.0 - cosA)) + (sqrtThird * sinA);
        var m11 = cosA + ((1.0 / 3.0) * (1.0 - cosA));
        var m12 = ((1.0 / 3.0) * (1.0 - cosA)) - (sqrtThird * sinA);

        var m20 = ((1.0 / 3.0) * (1.0 - cosA)) - (sqrtThird * sinA);
        var m21 = ((1.0 / 3.0) * (1.0 - cosA)) + (sqrtThird * sinA);
        var m22 = cosA + ((1.0 / 3.0) * (1.0 - cosA));

        return (
            (r * m00) + (g * m01) + (b * m02),
            (r * m10) + (g * m11) + (b * m12),
            (r * m20) + (g * m21) + (b * m22));
    }
}

/// <summary>
/// Draws a <c>width</c>-pixel stroke of <c>color</c> around non-transparent pixels of a layer (<c>outline</c>).
/// </summary>
public sealed class OutlineEffect : ILayerEffect
{
    public string EffectType => "outline";

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var outlineWidth = EffectPropertyHelpers.ResolveDouble(context, "width", "thickness", defaultValue: 2.0);
        if (outlineWidth <= 0.0 || !double.IsFinite(outlineWidth))
        {
            context.AddDiagnostic("FX005", $"Outline 'width' on layer '{context.LayerId}' must be a positive number.");
            return;
        }

        var outlineColor = EffectPropertyHelpers.ResolveColor(context, "color", ColorRgba.Black);
        var alphaThreshold = Math.Clamp(EffectPropertyHelpers.ResolveInt(context, "alpha_threshold", "threshold", defaultValue: 16), 1, 255);

        var width = target.Width;
        var height = target.Height;
        using var sourceCopy = target.Copy();

        var radiusCeil = (int)Math.Ceiling(outlineWidth);
        var radiusSq = outlineWidth * outlineWidth;

        for (var y = 0; y < height; y++)
        {
            var minY = Math.Max(0, y - radiusCeil);
            var maxY = Math.Min(height - 1, y + radiusCeil);

            for (var x = 0; x < width; x++)
            {
                var orig = sourceCopy.GetPixel(x, y);
                if (orig.Alpha == byte.MaxValue)
                {
                    continue;
                }

                var minX = Math.Max(0, x - radiusCeil);
                var maxX = Math.Min(width - 1, x + radiusCeil);
                var withinOutline = false;

                for (var ny = minY; ny <= maxY && !withinOutline; ny++)
                {
                    var dy = ny - y;
                    var dySq = dy * dy;
                    for (var nx = minX; nx <= maxX; nx++)
                    {
                        var dx = nx - x;
                        if ((dx * dx) + dySq <= radiusSq && sourceCopy.GetPixel(nx, ny).Alpha >= alphaThreshold)
                        {
                            withinOutline = true;
                            break;
                        }
                    }
                }

                if (!withinOutline)
                {
                    continue;
                }

                var srcRgba = ColorRgba.FromSKColor(orig);
                var blended = PixelBlender.BlendPixel(outlineColor, srcRgba, BlendMode.Normal);
                target.SetPixel(x, y, blended.ToSKColor());
            }
        }
    }
}

/// <summary>
/// Renders a shifted, optionally blurred, tinted silhouette behind a layer's alpha channel (<c>drop_shadow</c>).
/// </summary>
public sealed class DropShadowEffect(string effectType = "drop_shadow") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var offset = EffectPropertyHelpers.ResolvePoint(context, "offset", new Point2D(2, 2));
        var blur = EffectPropertyHelpers.ResolveDouble(context, "blur", "radius", defaultValue: 1.5);
        if (blur < 0.0 || !double.IsFinite(blur))
        {
            context.AddDiagnostic("FX006", $"Drop shadow 'blur' on layer '{context.LayerId}' must be non-negative.");
            blur = 0.0;
        }

        var shadowColor = EffectPropertyHelpers.ResolveColor(context, "color", new ColorRgba(0, 0, 0, 180));

        var width = target.Width;
        var height = target.Height;
        using var original = target.Copy();
        using var shadow = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        shadow.Erase(SKColors.Transparent);

        var offsetX = (int)Math.Round(offset.X);
        var offsetY = (int)Math.Round(offset.Y);

        for (var y = 0; y < height; y++)
        {
            var sy = y - offsetY;
            if (sy < 0 || sy >= height)
            {
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var sx = x - offsetX;
                if (sx < 0 || sx >= width)
                {
                    continue;
                }

                var srcPixel = original.GetPixel(sx, sy);
                if (srcPixel.Alpha == 0)
                {
                    continue;
                }

                var shadowAlpha = (byte)Math.Round(shadowColor.A * (srcPixel.Alpha / 255.0));
                shadow.SetPixel(x, y, new SKColor(shadowColor.R, shadowColor.G, shadowColor.B, shadowAlpha));
            }
        }

        if (blur > 1e-5)
        {
            using var unblurredShadow = shadow.Copy();
            shadow.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(shadow);
            using var filter = SKImageFilter.CreateBlur((float)blur, (float)blur);
            using var paint = new SKPaint
            {
                ImageFilter = filter,
                IsAntialias = true
            };
            canvas.DrawBitmap(unblurredShadow, 0, 0, paint);
            canvas.Flush();
        }

        PixelBlender.Composite(shadow, original, new Point2D(0, 0), BlendMode.Normal, 1.0f);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                target.SetPixel(x, y, shadow.GetPixel(x, y));
            }
        }
    }
}

internal static class EffectPropertyHelpers
{
    public static double ResolveDouble(EffectContext context, string primaryName, string? aliasName, double defaultValue)
    {
        if (context.Properties.TryGetProperty(primaryName, out var val)
            || (aliasName is not null && context.Properties.TryGetProperty(aliasName, out val)))
        {
            if (val is GfxNumber num)
            {
                return num.Value;
            }

            context.AddDiagnostic("FX007", $"Property '{primaryName}' on layer '{context.LayerId}' must be a number.");
        }

        return defaultValue;
    }

    public static int ResolveInt(EffectContext context, string primaryName, string? aliasName, int defaultValue)
    {
        if (context.Properties.TryGetProperty(primaryName, out var val)
            || (aliasName is not null && context.Properties.TryGetProperty(aliasName, out val)))
        {
            if (val is GfxNumber num)
            {
                return num.AsInt32();
            }

            context.AddDiagnostic("FX007", $"Property '{primaryName}' on layer '{context.LayerId}' must be an integer.");
        }

        return defaultValue;
    }

    public static bool ResolveBool(EffectContext context, string propName, bool defaultValue)
    {
        if (!context.Properties.TryGetProperty(propName, out var val))
        {
            return defaultValue;
        }

        if (val is GfxBoolean b)
        {
            return b.Value;
        }

        context.AddDiagnostic("FX007", $"Property '{propName}' on layer '{context.LayerId}' must be a boolean.");
        return defaultValue;
    }

    public static ColorRgba ResolveColor(EffectContext context, string propName, ColorRgba defaultValue)
    {
        if (!context.Properties.TryGetProperty(propName, out var val))
        {
            return defaultValue;
        }

        if (val is GfxColor color)
        {
            return color.Color;
        }

        context.AddDiagnostic("FX007", $"Property '{propName}' on layer '{context.LayerId}' must be a color.");
        return defaultValue;
    }

    public static Point2D ResolvePoint(EffectContext context, string propName, Point2D defaultValue)
    {
        if (!context.Properties.TryGetProperty(propName, out var val))
        {
            return defaultValue;
        }

        if (val.TryCoerceToPoint2D(out var pt))
        {
            return pt;
        }

        context.AddDiagnostic("FX007", $"Property '{propName}' on layer '{context.LayerId}' must be '{{ x, y }}'.");
        return defaultValue;
    }
}
