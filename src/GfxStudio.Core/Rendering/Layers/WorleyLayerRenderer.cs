using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Noise;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Rendering.Layers.Patterns;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>
/// Renders a procedural cellular / Worley (Voronoi) noise texture (<c>type = worley</c> / <c>voronoi</c>).
/// </summary>
public sealed class WorleyLayerRenderer : ILayerRenderer
{
    private static readonly ColorRamp DefaultColorRamp = new([
        new ColorStop(0.0f, ColorRgba.White),
        new ColorStop(1.0f, ColorRgba.Black)
    ]);

    public WorleyLayerRenderer(string layerType = "worley")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerType);
        LayerType = layerType;
    }

    public string LayerType { get; }

    public IReadOnlyCollection<string> SupportedProperties =>
        ["color", "cell_size", "scale", "frequency", "jitter", "distance_metric", "metric", "feature", "warp_strength", "warp_scale", "pattern_offset", "phase_offset"];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var colorRamp = ResolveColorRamp(context);
        if (colorRamp is null)
        {
            return bitmap;
        }

        var cellSize = ResolveCellSize(context, bitmap.Width, bitmap.Height);
        var jitter = ResolveDouble(context, "jitter", null, defaultValue: 1.0);
        var metric = ResolveDistanceMetric(context);
        var feature = ResolveFeature(context);
        var warpStrength = ResolveDouble(context, "warp_strength", null, defaultValue: 0.0);
        var warpScale = ResolvePositiveDouble(context, "warp_scale", null, defaultValue: 4.0);
        var (offsetX, offsetY) = StripesLayerRenderer.ResolvePatternOffset(context);

        var worley = new WorleyNoise(context.Random);
        PerlinNoise? warpNoiseX = null;
        PerlinNoise? warpNoiseY = null;
        if (Math.Abs(warpStrength) > 0.0)
        {
            warpNoiseX = new PerlinNoise(context.Random);
            warpNoiseY = new PerlinNoise(context.Random);
        }

        var width = bitmap.Width;
        var height = bitmap.Height;
        var invCellX = cellSize.X > 0 ? 1.0 / cellSize.X : 0.0;
        var invCellY = cellSize.Y > 0 ? 1.0 / cellSize.Y : 0.0;
        var invWarpW = width > 0 ? warpScale / width : 0.0;
        var invWarpH = height > 0 ? warpScale / height : 0.0;

        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var sy = y - offsetY;
            var baseNy = sy * invCellY;
            var wy = sy * invWarpH;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var sx = x - offsetX;
                var nx = sx * invCellX;
                var ny = baseNy;

                if (warpNoiseX is not null && warpNoiseY is not null)
                {
                    var wx = sx * invWarpW;
                    nx += warpStrength * warpNoiseX.Sample2D(wx, wy);
                    ny += warpStrength * warpNoiseY.Sample2D(wx, wy);
                }

                var value = worley.Sample2D(nx, ny, jitter, metric, feature);
                var sampled = colorRamp.Sample(value);
                var pixelIndex = rowOffset + (x * 4);

                WritePremulPixel(pixelBuffer, pixelIndex, sampled);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    private static (double X, double Y) ResolveCellSize(LayerRenderContext context, int width, int height)
    {
        if (context.LayerProperties.TryGetProperty("cell_size", out var cellVal))
        {
            if (cellVal is GfxNumber num && num.Value > 0.0)
            {
                return (num.Value, num.Value);
            }

            if (cellVal.TryCoerceToDimensions(out var dims))
            {
                return (dims.Width, dims.Height);
            }

            context.AddDiagnostic("RND014", $"Property 'cell_size' on layer '{context.LayerId}' must be a positive number or dimensions.");
            return (16.0, 16.0);
        }

        if (context.LayerProperties.TryGetProperty("scale", out var scaleVal)
            || context.LayerProperties.TryGetProperty("frequency", out scaleVal))
        {
            if (scaleVal is GfxNumber num && num.Value > 0.0)
            {
                // Support both cell_size-style (> 8 pixels per cell) and frequency-style scale
                if (num.Value >= 8.0 && !context.LayerProperties.Properties.ContainsKey("frequency"))
                {
                    return (num.Value, num.Value);
                }

                return (width / num.Value, height / num.Value);
            }
        }

        return (16.0, 16.0);
    }

    private static WorleyDistanceMetric ResolveDistanceMetric(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("distance_metric", out var val)
            && !context.LayerProperties.TryGetProperty("metric", out val))
        {
            return WorleyDistanceMetric.Euclidean;
        }

        var name = val switch
        {
            GfxSymbol s => s.Identifier,
            GfxString str => str.Value,
            _ => string.Empty
        };

        return name.ToLowerInvariant() switch
        {
            "euclidean" => WorleyDistanceMetric.Euclidean,
            "manhattan" or "taxicab" => WorleyDistanceMetric.Manhattan,
            "chebyshev" or "chessboard" => WorleyDistanceMetric.Chebyshev,
            _ => ReportInvalidEnum(context, "distance_metric", name, WorleyDistanceMetric.Euclidean)
        };
    }

    private static WorleyFeature ResolveFeature(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("feature", out var val))
        {
            return WorleyFeature.F1;
        }

        var name = val switch
        {
            GfxSymbol s => s.Identifier,
            GfxString str => str.Value,
            _ => string.Empty
        };

        return name.ToLowerInvariant() switch
        {
            "f1" => WorleyFeature.F1,
            "f2" => WorleyFeature.F2,
            "f2_minus_f1" or "f2-f1" or "edge" or "edges" => WorleyFeature.F2MinusF1,
            "cell_value" or "cell" or "voronoi" => WorleyFeature.CellValue,
            _ => ReportInvalidEnum(context, "feature", name, WorleyFeature.F1)
        };
    }

    private static T ReportInvalidEnum<T>(LayerRenderContext context, string propName, string actual, T fallback)
    {
        context.AddDiagnostic("RND014", $"Invalid '{propName}' value '{actual}' on layer '{context.LayerId}'.");
        return fallback;
    }

    private static ColorRamp? ResolveColorRamp(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("color", out var colorVal))
        {
            return DefaultColorRamp;
        }

        return colorVal switch
        {
            GfxColorRamp ramp => ramp.Ramp,
            GfxColor singleColor => new ColorRamp([
                new ColorStop(0.0f, ColorRgba.Black),
                new ColorStop(1.0f, singleColor.Color)
            ]),
            _ => ReportInvalidColor(context)
        };
    }

    private static ColorRamp? ReportInvalidColor(LayerRenderContext context)
    {
        context.AddDiagnostic(
            "RND013",
            $"Property 'color' on worley layer '{context.LayerId}' must be a color_ramp or color.");
        return null;
    }

    private static double ResolveDouble(LayerRenderContext context, string primaryName, string? aliasName, double defaultValue)
    {
        if (context.LayerProperties.TryGetProperty(primaryName, out var val)
            || (aliasName is not null && context.LayerProperties.TryGetProperty(aliasName, out val)))
        {
            if (val is GfxNumber num)
            {
                return num.Value;
            }

            context.AddDiagnostic("RND014", $"Property '{primaryName}' on layer '{context.LayerId}' must be a number.");
        }

        return defaultValue;
    }

    private static double ResolvePositiveDouble(LayerRenderContext context, string primaryName, string? aliasName, double defaultValue)
    {
        var value = ResolveDouble(context, primaryName, aliasName, defaultValue);
        if (value <= 0.0)
        {
            context.AddDiagnostic("RND014", $"Property '{primaryName}' on layer '{context.LayerId}' must be greater than zero.");
            return defaultValue;
        }

        return value;
    }

    internal static void WritePremulPixel(byte[] pixelBuffer, int pixelIndex, ColorRgba sampled)
    {
        if (sampled.A == byte.MaxValue)
        {
            pixelBuffer[pixelIndex] = sampled.R;
            pixelBuffer[pixelIndex + 1] = sampled.G;
            pixelBuffer[pixelIndex + 2] = sampled.B;
            pixelBuffer[pixelIndex + 3] = byte.MaxValue;
        }
        else if (sampled.A == 0)
        {
            pixelBuffer[pixelIndex] = 0;
            pixelBuffer[pixelIndex + 1] = 0;
            pixelBuffer[pixelIndex + 2] = 0;
            pixelBuffer[pixelIndex + 3] = 0;
        }
        else
        {
            var alphaFactor = sampled.A / 255.0f;
            pixelBuffer[pixelIndex] = (byte)Math.Round(sampled.R * alphaFactor);
            pixelBuffer[pixelIndex + 1] = (byte)Math.Round(sampled.G * alphaFactor);
            pixelBuffer[pixelIndex + 2] = (byte)Math.Round(sampled.B * alphaFactor);
            pixelBuffer[pixelIndex + 3] = sampled.A;
        }
    }
}
