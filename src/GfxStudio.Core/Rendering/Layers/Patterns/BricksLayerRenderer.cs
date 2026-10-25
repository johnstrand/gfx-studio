using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness.Seeding;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders a procedural masonry / brick wall pattern (<c>type = bricks</c>) with running, stack, or herringbone bond,
/// mortar joints, bevel shading, and deterministic per-brick color variation.
/// </summary>
public sealed class BricksLayerRenderer : ILayerRenderer
{
    public string LayerType => "bricks";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var brickW = ResolvePositiveDouble(context, "brick_width", defaultValue: 32.0);
        var brickH = ResolvePositiveDouble(context, "brick_height", defaultValue: 16.0);
        if (context.LayerProperties.TryGetProperty("brick_size", out var sizeVal) && sizeVal.TryCoerceToDimensions(out var dims))
        {
            brickW = Math.Max(2.0, dims.Width);
            brickH = Math.Max(2.0, dims.Height);
        }

        var mortar = Math.Max(0.0, ResolveDouble(context, "mortar_width", "mortar", defaultValue: 2.0));
        var bond = ResolveString(context, "bond", "style", defaultValue: "running").ToLowerInvariant();
        var defaultOffset = bond == "stack" ? 0.0 : 0.5;
        var rowOffset = ResolveDouble(context, "row_offset", null, defaultValue: defaultOffset);
        var bevel = Math.Clamp(ResolveDouble(context, "bevel", null, defaultValue: 0.15), 0.0, 1.0);
        var variation = Math.Clamp(ResolveDouble(context, "color_variation", "variation", defaultValue: 0.12), 0.0, 1.0);

        var baseColor = new ColorRgba(178, 76, 52, 255);
        ColorRamp? colorRamp = null;
        if (context.LayerProperties.TryGetProperty("color", out var colVal))
        {
            if (colVal is GfxColor c)
            {
                baseColor = c.Color;
            }
            else if (colVal is GfxColorRamp cr)
            {
                colorRamp = cr.Ramp;
            }
        }

        var mortarColor = CheckerboardLayerRenderer.ResolveColor(context, "mortar_color", new ColorRgba(210, 206, 198, 255));
        var layerSeed = unchecked((ulong)(context.Random.NextDouble() * ulong.MaxValue));

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];
        var halfMortar = mortar * 0.5;

        for (var y = 0; y < height; y++)
        {
            var rowIdx = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                int brickIdX, brickIdY;
                double distToEdgeX, distToEdgeY, curBrickW, curBrickH;

                if (bond == "herringbone")
                {
                    // 45-degree rotated herringbone step grid
                    var unit = brickH;
                    var u = (x + y) / Math.Sqrt(2.0);
                    var v = (y - x + 10000.0) / Math.Sqrt(2.0);
                    var gu = (int)Math.Floor(u / unit);
                    var gv = (int)Math.Floor(v / unit);
                    var parity = ((gu + gv) & 1) == 0;
                    if (parity)
                    {
                        brickIdX = gu / 2;
                        brickIdY = gv;
                        var localU = u - (brickIdX * 2 * unit);
                        var localV = v - (gv * unit);
                        curBrickW = unit * 2.0;
                        curBrickH = unit;
                        distToEdgeX = Math.Min(localU, curBrickW - localU);
                        distToEdgeY = Math.Min(localV, curBrickH - localV);
                    }
                    else
                    {
                        brickIdX = gu;
                        brickIdY = gv / 2;
                        var localU = u - (gu * unit);
                        var localV = v - (brickIdY * 2 * unit);
                        curBrickW = unit;
                        curBrickH = unit * 2.0;
                        distToEdgeX = Math.Min(localU, curBrickW - localU);
                        distToEdgeY = Math.Min(localV, curBrickH - localV);
                    }
                }
                else
                {
                    brickIdY = (int)Math.Floor(y / brickH);
                    var localY = y - (brickIdY * brickH);
                    var xShifted = x + (brickIdY * rowOffset * brickW);
                    brickIdX = (int)Math.Floor(xShifted / brickW);
                    var localX = xShifted - (brickIdX * brickW);
                    curBrickW = brickW;
                    curBrickH = brickH;
                    distToEdgeX = Math.Min(localX, brickW - localX);
                    distToEdgeY = Math.Min(localY, brickH - localY);
                }

                var minEdgeDist = Math.Min(distToEdgeX, distToEdgeY);
                ColorRgba pixelColor;

                if (minEdgeDist < halfMortar)
                {
                    pixelColor = mortarColor;
                }
                else
                {
                    var hash = HashBrick(brickIdX, brickIdY, layerSeed);
                    if (colorRamp is not null)
                    {
                        pixelColor = colorRamp.Sample((float)hash);
                    }
                    else
                    {
                        var delta = ((hash * 2.0) - 1.0) * variation;
                        pixelColor = AdjustBrightness(baseColor, delta);
                    }

                    if (bevel > 0.0)
                    {
                        var bevelWidth = Math.Max(1.0, Math.Min(curBrickW, curBrickH) * 0.25 * bevel);
                        var innerDist = minEdgeDist - halfMortar;
                        if (innerDist < bevelWidth)
                        {
                            var factor = innerDist / bevelWidth;
                            var shade = (factor - 1.0) * 0.35 * bevel;
                            pixelColor = AdjustBrightness(pixelColor, shade);
                        }
                    }
                }

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowIdx + (x * 4), pixelColor);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    private static double HashBrick(int bx, int by, ulong seed)
    {
        var h = seed ^ unchecked((ulong)bx * 0x9E3779B97F4A7C15UL) ^ unchecked((ulong)by * 0xBF58476D1CE4E5B9UL);
        var mixed = SeedMixer.Mix(h);
        return (mixed >> 11) * (1.0 / (1UL << 53));
    }

    private static ColorRgba AdjustBrightness(ColorRgba c, double delta)
    {
        var r = (byte)Math.Clamp(Math.Round(c.R + (delta * 255.0)), 0, 255);
        var g = (byte)Math.Clamp(Math.Round(c.G + (delta * 255.0)), 0, 255);
        var b = (byte)Math.Clamp(Math.Round(c.B + (delta * 255.0)), 0, 255);
        return new ColorRgba(r, g, b, c.A);
    }

    internal static double ResolvePositiveDouble(LayerRenderContext context, string name, double defaultValue)
    {
        if (context.LayerProperties.TryGetProperty(name, out var val) && val.TryCoerceToDouble(out var d) && d > 0.0 && double.IsFinite(d))
        {
            return d;
        }

        return defaultValue;
    }

    internal static double ResolveDouble(LayerRenderContext context, string primary, string? alias, double defaultValue)
    {
        if ((context.LayerProperties.TryGetProperty(primary, out var val) ||
             (alias is not null && context.LayerProperties.TryGetProperty(alias, out val)))
            && val.TryCoerceToDouble(out var d) && double.IsFinite(d))
        {
            return d;
        }

        return defaultValue;
    }

    internal static string ResolveString(LayerRenderContext context, string primary, string? alias, string defaultValue)
    {
        if (context.LayerProperties.TryGetProperty(primary, out var val) ||
            (alias is not null && context.LayerProperties.TryGetProperty(alias, out val)))
        {
            return val switch
            {
                GfxString s => s.Value,
                GfxSymbol sym => sym.Identifier,
                _ => defaultValue
            };
        }

        return defaultValue;
    }
}
