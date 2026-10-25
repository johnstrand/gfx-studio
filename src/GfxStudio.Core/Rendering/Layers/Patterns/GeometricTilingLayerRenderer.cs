using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness.Seeding;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders geometric tessellations (<c>type = tiling</c>, <c>hex_grid</c>, <c>chevron</c>) including
/// hexagonal grids, equilateral triangles, isometric cubes, and chevron zigzags.
/// </summary>
public sealed class GeometricTilingLayerRenderer(string layerType = "tiling", string defaultStyle = "hex") : ILayerRenderer
{
    public string LayerType { get; } = layerType;

    public IReadOnlyCollection<string> SupportedProperties =>
    [
        "style",
        "pattern",
        "cell_size",
        "size",
        "line_width",
        "stroke_width",
        "stroke_color",
        "color",
        "fill_color",
        "color_variation",
        "variation"
    ];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var style = BricksLayerRenderer.ResolveString(context, "style", "pattern", defaultValue: defaultStyle).ToLowerInvariant();
        var cellSize = Math.Max(4.0, BricksLayerRenderer.ResolveDouble(context, "cell_size", "size", defaultValue: 20.0));
        var lineWidth = Math.Max(0.0, BricksLayerRenderer.ResolveDouble(context, "line_width", "stroke_width", defaultValue: 1.5));
        var strokeColor = CheckerboardLayerRenderer.ResolveColor(context, "stroke_color", new ColorRgba(20, 24, 33, 255));
        var baseFillColor = new ColorRgba(70, 130, 180, 255);
        ColorRamp? colorRamp = null;

        if (context.LayerProperties.TryGetProperty("color", out var colVal)
            || context.LayerProperties.TryGetProperty("fill_color", out colVal))
        {
            if (colVal is GfxColor c)
            {
                baseFillColor = c.Color;
            }
            else if (colVal is GfxColorRamp cr)
            {
                colorRamp = cr.Ramp;
            }
        }

        var variation = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "color_variation", "variation", defaultValue: 0.15), 0.0, 1.0);
        var layerSeed = unchecked((ulong)(context.Random.NextDouble() * ulong.MaxValue));
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];
        var halfLine = lineWidth * 0.5;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;

                int cellIdX, cellIdY;
                double edgeDist;
                var faceShade = 0.0;

                switch (style)
                {
                    case "triangle" or "triangles":
                        (cellIdX, cellIdY, edgeDist) = SampleTriangleGrid(px, py, cellSize);
                        break;

                    case "isometric" or "cubes" or "isometric_cubes":
                        (cellIdX, cellIdY, edgeDist, faceShade) = SampleIsometricCubes(px, py, cellSize);
                        break;

                    case "chevron" or "zigzag":
                        (cellIdX, cellIdY, edgeDist) = SampleChevron(px, py, cellSize);
                        break;

                    default: // "hex" or "hexagon" or "honeycomb"
                        (cellIdX, cellIdY, edgeDist) = SampleHexGrid(px, py, cellSize);
                        break;
                }

                var hash = HashCell(cellIdX, cellIdY, layerSeed);
                ColorRgba cellColor;
                if (colorRamp is not null)
                {
                    cellColor = colorRamp.Sample((float)hash);
                }
                else
                {
                    var delta = ((hash * 2.0) - 1.0) * variation;
                    cellColor = AdjustColor(baseFillColor, delta);
                }

                if (Math.Abs(faceShade) > 1e-4)
                {
                    cellColor = AdjustColor(cellColor, faceShade);
                }

                ColorRgba finalColor;
                if (lineWidth > 0.0)
                {
                    var strokeFactor = (float)Math.Clamp(halfLine + 0.5 - edgeDist, 0.0, 1.0);
                    finalColor = TruchetLayerRenderer.LerpColor(cellColor, strokeColor, strokeFactor);
                }
                else
                {
                    finalColor = cellColor;
                }

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), finalColor);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    private static (int IdX, int IdY, double EdgeDist) SampleHexGrid(double px, double py, double r)
    {
        // Axial/cube coordinates for pointy-top hexagons of circumradius r
        var sqrt3 = Math.Sqrt(3.0);
        var q = ((sqrt3 / 3.0 * px) - (1.0 / 3.0 * py)) / r;
        var s = (2.0 / 3.0 * py) / r;
        var c = -q - s;

        var rq = Math.Round(q);
        var rs = Math.Round(s);
        var rc = Math.Round(c);

        var dq = Math.Abs(rq - q);
        var ds = Math.Abs(rs - s);
        var dc = Math.Abs(rc - c);

        if (dq > ds && dq > dc)
        {
            rq = -rs - rc;
        }
        else if (ds > dc)
        {
            rs = -rq - rc;
        }

        var centerX = r * sqrt3 * (rq + (rs * 0.5));
        var centerY = r * 1.5 * rs;

        var dx = Math.Abs(px - centerX);
        var dy = Math.Abs(py - centerY);
        var apothem = r * sqrt3 * 0.5;
        var hexDist = Math.Max(dx, (dx * 0.5) + (dy * sqrt3 * 0.5));
        var edgeDist = Math.Max(0.0, apothem - hexDist);

        return ((int)rq, (int)rs, edgeDist);
    }

    private static (int IdX, int IdY, double EdgeDist) SampleTriangleGrid(double px, double py, double side)
    {
        var h = side * Math.Sqrt(3.0) * 0.5;
        var row = (int)Math.Floor(py / h);
        var ly = py - (row * h);
        var shiftedX = px - (row * side * 0.5);
        var col = (int)Math.Floor(shiftedX / side);
        var lx = shiftedX - (col * side);

        var tY = ly / h;
        var tX = lx / side;
        var isUpper = (tX + tY) > 1.0;

        double d1, d2, d3;
        if (!isUpper)
        {
            d1 = ly;
            d2 = ((tX + tY) * h * 0.5) * Math.Sqrt(3.0);
            d3 = ((1.0 - tX - tY) * side) * (Math.Sqrt(3.0) * 0.5);
            d2 = ((lx * Math.Sqrt(3.0)) + ly) * 0.5;
        }
        else
        {
            d1 = h - ly;
            d2 = (((1.0 - tX) * Math.Sqrt(3.0)) + (1.0 - tY)) * side * 0.433;
            d3 = ((tX + tY - 1.0) * side) * (Math.Sqrt(3.0) * 0.5);
        }

        var edgeDist = Math.Max(0.0, Math.Min(d1, Math.Min(d2, d3)));
        return ((col * 2) + (isUpper ? 1 : 0), row, edgeDist);
    }

    private static (int IdX, int IdY, double EdgeDist, double FaceShade) SampleIsometricCubes(double px, double py, double r)
    {
        var (hx, hy, hexEdgeDist) = SampleHexGrid(px, py, r);
        var sqrt3 = Math.Sqrt(3.0);
        var centerX = r * sqrt3 * (hx + (hy * 0.5));
        var centerY = r * 1.5 * hy;
        var dx = px - centerX;
        var dy = py - centerY;

        var angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
        if (angle < 0) angle += 360.0;

        // 3 diamond faces meeting at the center of each hexagon (-30..90, 90..210, 210..330)
        int faceIndex;
        double shade;
        if (angle is >= 210.0 and < 330.0)
        {
            faceIndex = 0; // Top face (brightest)
            shade = 0.18;
        }
        else if (angle >= 90.0 && angle < 210.0)
        {
            faceIndex = 1; // Left face (medium)
            shade = -0.02;
        }
        else
        {
            faceIndex = 2; // Right face (darkest)
            shade = -0.20;
        }

        // Distance to the 3 internal spokes at -30 deg, 90 deg, 210 deg
        var spoke1 = dy >= 0 ? Math.Abs(dx) : Math.Sqrt((dx * dx) + (dy * dy));
        var u2 = (dx * 0.5) + (dy * sqrt3 * 0.5);
        var v2 = (-dx * sqrt3 * 0.5) + (dy * 0.5);
        var spoke2 = v2 <= 0 ? Math.Abs(u2) : Math.Sqrt((u2 * u2) + (v2 * v2));
        var u3 = (-dx * 0.5) + (dy * sqrt3 * 0.5);
        var v3 = (-dx * sqrt3 * 0.5) - (dy * 0.5);
        var spoke3 = v3 <= 0 ? Math.Abs(u3) : Math.Sqrt((u3 * u3) + (v3 * v3));

        var internalDist = Math.Min(spoke1, Math.Min(spoke2, spoke3));
        var edgeDist = Math.Min(hexEdgeDist, internalDist);
        return ((hx * 3) + faceIndex, hy, edgeDist, shade);
    }

    private static (int IdX, int IdY, double EdgeDist) SampleChevron(double px, double py, double size)
    {
        var col = (int)Math.Floor(px / size);
        var lx = px - (col * size);
        var zig = (col & 1) == 0 ? lx : (size - lx);
        var shiftedY = py + zig;
        var row = (int)Math.Floor(shiftedY / size);
        var ly = shiftedY - (row * size);

        var distY = Math.Min(ly, size - ly) / Math.Sqrt(2.0);
        var distX = Math.Min(lx, size - lx);
        return (0, row, Math.Min(distY, distX));
    }

    private static double HashCell(int cx, int cy, ulong seed)
    {
        var h = seed ^ unchecked((ulong)cx * 0x9E3779B97F4A7C15UL) ^ unchecked((ulong)cy * 0xBF58476D1CE4E5B9UL);
        var mixed = SeedMixer.Mix(h);
        return (mixed >> 11) * (1.0 / (1UL << 53));
    }

    private static ColorRgba AdjustColor(ColorRgba c, double delta)
    {
        var r = (byte)Math.Clamp(Math.Round(c.R + (delta * 255.0)), 0, 255);
        var g = (byte)Math.Clamp(Math.Round(c.G + (delta * 255.0)), 0, 255);
        var b = (byte)Math.Clamp(Math.Round(c.B + (delta * 255.0)), 0, 255);
        return new ColorRgba(r, g, b, c.A);
    }
}
