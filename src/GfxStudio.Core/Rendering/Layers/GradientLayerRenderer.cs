using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>Renders linear, radial, or conic color-ramp gradients.</summary>
public sealed class GradientLayerRenderer : ILayerRenderer
{
    public string LayerType => "gradient";

    public IReadOnlyCollection<string> SupportedProperties => ["color", "style", "angle", "center", "radius"];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bitmap = context.CreateLayerBitmap();
        if (!context.LayerProperties.TryGetProperty("color", out var colorValue) || colorValue is not GfxColorRamp gfxRamp)
        {
            context.AddDiagnostic("RND047", $"Gradient layer '{context.LayerId}' requires a color_ramp in property 'color'.");
            return bitmap;
        }

        var style = ResolveStyle(context);
        if (style is null)
        {
            return bitmap;
        }

        var angle = ResolveNumber(context, "angle", 0.0);
        var center = ResolvePoint(context, "center", new Point2D(bitmap.Width / 2.0f, bitmap.Height / 2.0f));
        var fallbackRadius = FarthestCornerRadius(bitmap.Width, bitmap.Height, center);
        var radius = ResolveNumber(context, "radius", fallbackRadius);
        if (radius <= 0.0 || !double.IsFinite(radius))
        {
            context.AddDiagnostic("RND048", $"Gradient radius on layer '{context.LayerId}' must be a positive finite number.");
            return bitmap;
        }

        var radians = angle * Math.PI / 180.0;
        var directionX = Math.Cos(radians);
        var directionY = Math.Sin(radians);
        var linearRange = (Math.Abs(directionX) * bitmap.Width) + (Math.Abs(directionY) * bitmap.Height);
        var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var sampleX = x + 0.5 - center.X;
                var sampleY = y + 0.5 - center.Y;
                var t = style.Value switch
                {
                    GradientStyle.Linear => 0.5 + ((sampleX * directionX + sampleY * directionY) / linearRange),
                    GradientStyle.Radial => Math.Sqrt(sampleX * sampleX + sampleY * sampleY) / radius,
                    GradientStyle.Conic => ConicPosition(sampleX, sampleY, radians),
                    _ => 0.0
                };

                WriteColor(pixels, (y * bitmap.Width + x) * 4, gfxRamp.Ramp.Sample((float)Math.Clamp(t, 0.0, 1.0)));
            }
        }

        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    private static GradientStyle? ResolveStyle(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("style", out var value))
        {
            return GradientStyle.Linear;
        }

        var name = value switch
        {
            GfxSymbol symbol => symbol.Identifier,
            GfxString text => text.Value,
            _ => string.Empty
        };

        return name.ToLowerInvariant() switch
        {
            "linear" => GradientStyle.Linear,
            "radial" => GradientStyle.Radial,
            "conic" or "angular" or "sweep" => GradientStyle.Conic,
            _ => ReportInvalidStyle(context, name)
        };
    }

    private static GradientStyle? ReportInvalidStyle(LayerRenderContext context, string style)
    {
        context.AddDiagnostic("RND049", $"Unknown gradient style '{style}' on layer '{context.LayerId}'. Expected linear, radial, or conic.");
        return null;
    }

    private static double ResolveNumber(LayerRenderContext context, string propertyName, double defaultValue)
    {
        if (!context.LayerProperties.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        if (value is GfxNumber number && double.IsFinite(number.Value))
        {
            return number.Value;
        }

        context.AddDiagnostic("RND052", $"Property '{propertyName}' on gradient layer '{context.LayerId}' must be a finite number.");
        return defaultValue;
    }

    private static Point2D ResolvePoint(LayerRenderContext context, string propertyName, Point2D defaultValue)
    {
        if (!context.LayerProperties.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        if (value.TryCoerceToPoint2D(out var point) && float.IsFinite(point.X) && float.IsFinite(point.Y))
        {
            return point;
        }

        context.AddDiagnostic("RND053", $"Property '{propertyName}' on gradient layer '{context.LayerId}' must contain finite x and y values.");
        return defaultValue;
    }

    private static double FarthestCornerRadius(int width, int height, Point2D center)
    {
        var maxX = Math.Max(Math.Abs(center.X), Math.Abs(width - center.X));
        var maxY = Math.Max(Math.Abs(center.Y), Math.Abs(height - center.Y));
        return Math.Sqrt((maxX * maxX) + (maxY * maxY));
    }

    private static double ConicPosition(double x, double y, double rotationRadians)
    {
        var angle = Math.Atan2(y, x) + (Math.PI / 2.0) - rotationRadians;
        var turns = angle / Math.Tau;
        return turns - Math.Floor(turns);
    }

    private static void WriteColor(byte[] pixels, int offset, ColorRgba color)
    {
        var alphaFactor = color.A / 255.0f;
        pixels[offset] = (byte)Math.Round(color.R * alphaFactor);
        pixels[offset + 1] = (byte)Math.Round(color.G * alphaFactor);
        pixels[offset + 2] = (byte)Math.Round(color.B * alphaFactor);
        pixels[offset + 3] = color.A;
    }

    private enum GradientStyle
    {
        Linear,
        Radial,
        Conic
    }
}
