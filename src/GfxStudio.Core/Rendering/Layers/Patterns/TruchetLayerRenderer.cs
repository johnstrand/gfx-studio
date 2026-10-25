using System.Runtime.InteropServices;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness.Seeding;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders deterministic Truchet maze/curve tessellations (<c>type = truchet</c>) with quarter-circle arcs,
/// diagonal maze lines, or split-triangle tiles.
/// </summary>
public sealed class TruchetLayerRenderer : ILayerRenderer
{
    public string LayerType => "truchet";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var (cellW, cellH) = CheckerboardLayerRenderer.ResolveTileSize(context, "cell_size", "tile_size", defaultSize: 16);
        var cellSize = Math.Max(4.0, Math.Min(cellW, cellH));
        var style = BricksLayerRenderer.ResolveString(context, "style", "pattern", defaultValue: "arcs").ToLowerInvariant();
        var lineWidth = Math.Max(0.5, BricksLayerRenderer.ResolveDouble(context, "line_width", "width", defaultValue: Math.Max(2.0, cellSize * 0.15)));
        var fgColor = CheckerboardLayerRenderer.ResolveColor(context, "color", ColorRgba.White);
        var bgColor = CheckerboardLayerRenderer.ResolveColor(context, "background", new ColorRgba(0, 0, 0, 255));
        var layerSeed = unchecked((ulong)(context.Random.NextDouble() * ulong.MaxValue));

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];
        var halfLine = lineWidth * 0.5;
        var radius = cellSize * 0.5;

        for (var y = 0; y < height; y++)
        {
            var cy = (int)Math.Floor(y / cellSize);
            var ly = (y + 0.5) - (cy * cellSize);
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var cx = (int)Math.Floor(x / cellSize);
                var lx = (x + 0.5) - (cx * cellSize);

                var hash = SeedMixer.Mix(layerSeed ^ unchecked((ulong)cx * 0x9E3779B97F4A7C15UL) ^ unchecked((ulong)cy * 0xBF58476D1CE4E5B9UL));
                var flip = (hash & 1UL) != 0;

                double coverage;
                if (style is "triangles" or "triangle")
                {
                    var orient = (int)(hash & 3UL);
                    var d = orient switch
                    {
                        0 => lx + ly - cellSize,
                        1 => lx - ly,
                        2 => cellSize - lx - ly,
                        _ => ly - lx
                    };
                    coverage = Math.Clamp(0.5 - d, 0.0, 1.0);
                }
                else if (style is "diagonal" or "maze" or "lines")
                {
                    // Distance to main or anti-diagonal
                    var dist1 = flip
                        ? Math.Abs(lx + ly - cellSize) / Math.Sqrt(2.0)
                        : Math.Abs(lx - ly) / Math.Sqrt(2.0);
                    coverage = Math.Clamp(halfLine + 0.5 - dist1, 0.0, 1.0);
                }
                else
                {
                    // Quarter-circle Smith Truchet arcs around two opposite corners
                    double d1, d2;
                    if (!flip)
                    {
                        // Corners (0,0) and (cellSize, cellSize)
                        d1 = Math.Abs(Math.Sqrt((lx * lx) + (ly * ly)) - radius);
                        var dx2 = lx - cellSize;
                        var dy2 = ly - cellSize;
                        d2 = Math.Abs(Math.Sqrt((dx2 * dx2) + (dy2 * dy2)) - radius);
                    }
                    else
                    {
                        // Corners (cellSize, 0) and (0, cellSize)
                        var dx1 = lx - cellSize;
                        d1 = Math.Abs(Math.Sqrt((dx1 * dx1) + (ly * ly)) - radius);
                        var dy2 = ly - cellSize;
                        d2 = Math.Abs(Math.Sqrt((lx * lx) + (dy2 * dy2)) - radius);
                    }

                    var minDist = Math.Min(d1, d2);
                    coverage = Math.Clamp(halfLine + 0.5 - minDist, 0.0, 1.0);
                }

                var finalColor = LerpColor(bgColor, fgColor, (float)coverage);
                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), finalColor);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    internal static ColorRgba LerpColor(ColorRgba a, ColorRgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new ColorRgba(
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)),
            (byte)Math.Round(a.A + ((b.A - a.A) * t)));
    }
}
