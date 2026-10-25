using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Rendering.Layers.Patterns;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>
/// Renders composable 2D Signed Distance Field shapes (<c>type = sdf</c>) with smooth CSG boolean operations
/// (<c>union</c>, <c>smooth_union</c>, <c>subtract</c>, <c>intersect</c>, <c>xor</c>), anti-aliased strokes,
/// outer glow, and contour/isoline distance field visualization.
/// </summary>
public sealed class SdfLayerRenderer : ILayerRenderer
{
    public string LayerType => "sdf";

    public IReadOnlyCollection<string> SupportedProperties =>
    [
        "color",
        "fill_color",
        "stroke_color",
        "stroke_width",
        "glow_color",
        "glow_radius",
        "glow",
        "background",
        "mode",
        "style",
        "bands",
        "contours",
        "shapes",
        "shape",
        "primitive",
        "center",
        "radius",
        "size",
        "width",
        "height",
        "corner_radius",
        "rotation",
        "vertices",
        "inner_radius",
        "thickness",
        "start",
        "end",
        "op",
        "operation",
        "smoothness",
        "k",
        "onion"
    ];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var width = bitmap.Width;
        var height = bitmap.Height;
        var shapes = ParseShapes(context, width, height);

        ColorRamp? colorRamp = null;
        var fillColor = ColorRgba.White;
        var hasFill = false;
        if (context.LayerProperties.TryGetProperty("color", out var colVal)
            || context.LayerProperties.TryGetProperty("fill_color", out colVal))
        {
            if (colVal is GfxColorRamp cr)
            {
                colorRamp = cr.Ramp;
            }
            else if (colVal is GfxColor c)
            {
                fillColor = c.Color;
                hasFill = true;
            }
        }

        if (!hasFill && colorRamp is null)
        {
            hasFill = true;
        }

        var strokeColor = CheckerboardLayerRenderer.ResolveColor(context, "stroke_color", ColorRgba.Black);
        var strokeWidth = Math.Max(0.0, BricksLayerRenderer.ResolveDouble(context, "stroke_width", null, defaultValue: 0.0));
        var glowColor = CheckerboardLayerRenderer.ResolveColor(context, "glow_color", fillColor);
        var glowRadius = Math.Max(0.0, BricksLayerRenderer.ResolveDouble(context, "glow_radius", "glow", defaultValue: 0.0));
        var bgColor = CheckerboardLayerRenderer.ResolveColor(context, "background", new ColorRgba(0, 0, 0, 0));
        var mode = BricksLayerRenderer.ResolveString(context, "mode", "style", defaultValue: colorRamp is not null ? "isoline" : "shape").ToLowerInvariant();
        var bands = Math.Max(0.0, BricksLayerRenderer.ResolveDouble(context, "bands", "contours", defaultValue: mode == "isoline" ? 8.0 : 0.0));
        var maxDist = Math.Max(1.0, Math.Min(width, height) * 0.5);

        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var py = y + 0.5;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var px = x + 0.5;
                var d = EvaluateSceneDistance(px, py, shapes);

                ColorRgba pixel;
                if (colorRamp is not null || mode == "isoline")
                {
                    var norm = Math.Clamp((d / maxDist) * 0.5 + 0.5, 0.0, 1.0);
                    var baseCol = colorRamp is not null
                        ? colorRamp.Sample((float)norm)
                        : (d <= 0.0 ? fillColor : bgColor);

                    if (bands > 0.0)
                    {
                        var wave = 0.75 + (0.25 * Math.Cos(d * bands * (Math.Tau / maxDist)));
                        var borderHighlight = Math.Exp(-Math.Abs(d) * 0.6);
                        var r = (byte)Math.Clamp(Math.Round((baseCol.R * wave) + (borderHighlight * 180.0)), 0, 255);
                        var g = (byte)Math.Clamp(Math.Round((baseCol.G * wave) + (borderHighlight * 180.0)), 0, 255);
                        var b = (byte)Math.Clamp(Math.Round((baseCol.B * wave) + (borderHighlight * 180.0)), 0, 255);
                        pixel = new ColorRgba(r, g, b, 255);
                    }
                    else
                    {
                        pixel = baseCol;
                    }
                }
                else
                {
                    pixel = bgColor;

                    // 1. Outer glow
                    if (glowRadius > 0.0 && d > -0.5)
                    {
                        var glowFactor = (float)Math.Clamp(Math.Exp(-Math.Max(0.0, d) / (glowRadius * 0.45)), 0.0, 1.0);
                        var glowCol = new ColorRgba(glowColor.R, glowColor.G, glowColor.B, (byte)Math.Round(glowColor.A * glowFactor));
                        pixel = BlendOver(pixel, glowCol);
                    }

                    // 2. Anti-aliased shape interior fill
                    if (hasFill)
                    {
                        var fillAlpha = (float)Math.Clamp(0.5 - d, 0.0, 1.0);
                        if (fillAlpha > 0f)
                        {
                            var fc = new ColorRgba(fillColor.R, fillColor.G, fillColor.B, (byte)Math.Round(fillColor.A * fillAlpha));
                            pixel = BlendOver(pixel, fc);
                        }
                    }

                    // 3. Anti-aliased stroke contour around d = 0
                    if (strokeWidth > 0.0)
                    {
                        var halfStroke = strokeWidth * 0.5;
                        var strokeAlpha = (float)Math.Clamp(halfStroke + 0.5 - Math.Abs(d), 0.0, 1.0);
                        if (strokeAlpha > 0f)
                        {
                            var sc = new ColorRgba(strokeColor.R, strokeColor.G, strokeColor.B, (byte)Math.Round(strokeColor.A * strokeAlpha));
                            pixel = BlendOver(pixel, sc);
                        }
                    }
                }

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), pixel);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    private static ColorRgba BlendOver(ColorRgba dst, ColorRgba src)
    {
        if (src.A == 0) return dst;
        if (src.A == 255 || dst.A == 0) return src;
        var sa = src.A / 255.0;
        var da = dst.A / 255.0;
        var outA = sa + (da * (1.0 - sa));
        if (outA <= 1e-6) return new ColorRgba(0, 0, 0, 0);
        var r = (byte)Math.Clamp(Math.Round(((src.R * sa) + (dst.R * da * (1.0 - sa))) / outA), 0, 255);
        var g = (byte)Math.Clamp(Math.Round(((src.G * sa) + (dst.G * da * (1.0 - sa))) / outA), 0, 255);
        var b = (byte)Math.Clamp(Math.Round(((src.B * sa) + (dst.B * da * (1.0 - sa))) / outA), 0, 255);
        var a = (byte)Math.Clamp(Math.Round(outA * 255.0), 0, 255);
        return new ColorRgba(r, g, b, a);
    }

    private static double EvaluateSceneDistance(double px, double py, List<SdfPrimitive> shapes)
    {
        if (shapes.Count == 0)
        {
            return 1000.0;
        }

        var d = EvaluatePrimitive(px, py, shapes[0]);
        for (var i = 1; i < shapes.Count; i++)
        {
            var s = shapes[i];
            var d2 = EvaluatePrimitive(px, py, s);
            d = CombineDistance(d, d2, s.Operation, s.Smoothness);
        }

        return d;
    }

    private static double EvaluatePrimitive(double px, double py, SdfPrimitive s)
    {
        var lx = px - s.CenterX;
        var ly = py - s.CenterY;

        if (Math.Abs(s.RotationRad) > 1e-6)
        {
            var cosR = Math.Cos(-s.RotationRad);
            var sinR = Math.Sin(-s.RotationRad);
            var rx = (lx * cosR) - (ly * sinR);
            var ry = (lx * sinR) + (ly * cosR);
            lx = rx;
            ly = ry;
        }

        double d;
        switch (s.ShapeType)
        {
            case "box" or "rect" or "rounded_box":
            {
                var qx = Math.Abs(lx) - s.HalfWidth + s.CornerRadius;
                var qy = Math.Abs(ly) - s.HalfHeight + s.CornerRadius;
                var outDist = Math.Sqrt((Math.Max(qx, 0.0) * Math.Max(qx, 0.0)) + (Math.Max(qy, 0.0) * Math.Max(qy, 0.0)));
                var inDist = Math.Min(Math.Max(qx, qy), 0.0);
                d = outDist + inDist - s.CornerRadius;
                break;
            }

            case "ring" or "annulus":
            {
                d = Math.Abs(Math.Sqrt((lx * lx) + (ly * ly)) - s.Radius) - s.Thickness;
                break;
            }

            case "segment" or "line":
            {
                var pax = px - s.StartX;
                var pay = py - s.StartY;
                var bax = s.EndX - s.StartX;
                var bay = s.EndY - s.StartY;
                var denom = (bax * bax) + (bay * bay);
                var h = denom > 1e-6 ? Math.Clamp(((pax * bax) + (pay * bay)) / denom, 0.0, 1.0) : 0.0;
                var dx = pax - (bax * h);
                var dy = pay - (bay * h);
                d = Math.Sqrt((dx * dx) + (dy * dy)) - s.Thickness;
                break;
            }

            case "polygon":
            {
                var n = Math.Max(3, s.Vertices);
                var an = Math.PI / n;
                var angle = Math.Atan2(lx, -ly);
                var bn = Mod(angle, 2.0 * an) - an;
                var len = Math.Sqrt((lx * lx) + (ly * ly));
                d = (len * Math.Cos(bn)) - (s.Radius * Math.Cos(an));
                break;
            }

            case "star":
            {
                var n = Math.Max(3, s.Vertices);
                var an = Math.PI / n;
                var angle = Math.Atan2(lx, -ly);
                var bn = Math.Abs(Mod(angle, 2.0 * an) - an);
                var len = Math.Sqrt((lx * lx) + (ly * ly));
                var t = bn / an;
                var starR = (s.Radius * (1.0 - t)) + (s.InnerRadius * t);
                d = len - starR;
                break;
            }

            case "cross" or "plus":
            {
                var ax = Math.Abs(lx);
                var ay = Math.Abs(ly);
                var dHoriz = Math.Max(ax - s.Radius, ay - s.Thickness);
                var dVert = Math.Max(ax - s.Thickness, ay - s.Radius);
                d = Math.Min(dHoriz, dVert);
                break;
            }

            case "vesica":
            {
                var offset = s.Radius * 0.5;
                var d1 = Math.Sqrt(((Math.Abs(lx) + offset) * (Math.Abs(lx) + offset)) + (ly * ly)) - s.Radius;
                d = d1;
                break;
            }

            default: // "circle"
            {
                d = Math.Sqrt((lx * lx) + (ly * ly)) - s.Radius;
                break;
            }
        }

        if (s.Onion > 0.0)
        {
            d = Math.Abs(d) - s.Onion;
        }

        return d;
    }

    private static double Mod(double a, double b) => a - (b * Math.Floor(a / b));

    private static double CombineDistance(double d1, double d2, string op, double k)
    {
        var smoothK = Math.Max(0.001, k);
        return op switch
        {
            "smooth_union" or "smin" => SmoothUnion(d1, d2, smoothK),
            "subtract" or "difference" => Math.Max(d1, -d2),
            "smooth_subtract" => -SmoothUnion(-d1, d2, smoothK),
            "intersect" or "intersection" => Math.Max(d1, d2),
            "smooth_intersect" => -SmoothUnion(-d1, -d2, smoothK),
            "xor" => Math.Max(Math.Min(d1, d2), -Math.Max(d1, d2)),
            _ => Math.Min(d1, d2) // "union"
        };
    }

    private static double SmoothUnion(double d1, double d2, double k)
    {
        var h = Math.Clamp(0.5 + (0.5 * (d2 - d1) / k), 0.0, 1.0);
        return (d2 * (1.0 - h)) + (d1 * h) - (k * h * (1.0 - h));
    }

    private static List<SdfPrimitive> ParseShapes(LayerRenderContext context, int width, int height)
    {
        var list = new List<SdfPrimitive>();
        if (context.LayerProperties.TryGetProperty("shapes", out var shapesVal) && shapesVal is GfxArray arr)
        {
            foreach (var item in arr.Items)
            {
                if (item is GfxObject obj)
                {
                    list.Add(ParseOneShape(obj, width, height));
                }
            }
        }

        if (list.Count == 0)
        {
            list.Add(ParseOneShape(context.LayerProperties, width, height));
        }

        return list;
    }

    private static SdfPrimitive ParseOneShape(GfxObject props, int width, int height)
    {
        var shapeType = "circle";
        if (props.TryGetProperty("shape", out var stVal) || props.TryGetProperty("primitive", out stVal))
        {
            shapeType = stVal switch
            {
                GfxString s => s.Value.ToLowerInvariant(),
                GfxSymbol sym => sym.Identifier.ToLowerInvariant(),
                _ => "circle"
            };
        }

        var cx = width * 0.5;
        var cy = height * 0.5;
        if ((props.TryGetProperty("center", out var cVal) || props.TryGetProperty("offset", out cVal)) && cVal.TryCoerceToPoint2D(out var pt))
        {
            cx = pt.X;
            cy = pt.Y;
        }

        var minDim = Math.Min(width, height);
        var radius = minDim * 0.3;
        if (props.TryGetProperty("radius", out var rVal) && rVal.TryCoerceToDouble(out var rNum))
        {
            radius = Math.Max(0.5, rNum);
        }

        var halfW = radius;
        var halfH = radius;
        if (props.TryGetProperty("size", out var szVal))
        {
            if (szVal.TryCoerceToDimensions(out var dims))
            {
                halfW = dims.Width * 0.5;
                halfH = dims.Height * 0.5;
            }
            else if (szVal.TryCoerceToDouble(out var sNum))
            {
                halfW = sNum * 0.5;
                halfH = sNum * 0.5;
            }
        }

        if (props.TryGetProperty("width", out var wVal) && wVal.TryCoerceToDouble(out var wNum)) halfW = wNum * 0.5;
        if (props.TryGetProperty("height", out var hVal) && hVal.TryCoerceToDouble(out var hNum)) halfH = hNum * 0.5;

        var cornerRadius = 0.0;
        if (props.TryGetProperty("corner_radius", out var crVal) && crVal.TryCoerceToDouble(out var crNum))
        {
            cornerRadius = Math.Max(0.0, crNum);
        }

        var rotationRad = 0.0;
        if (props.TryGetProperty("rotation", out var rotVal) && rotVal.TryCoerceToDouble(out var rotDeg))
        {
            rotationRad = rotDeg * (Math.PI / 180.0);
        }

        var vertices = 5;
        if (props.TryGetProperty("vertices", out var vVal) && vVal.TryCoerceToDouble(out var vNum))
        {
            vertices = Math.Max(3, (int)vNum);
        }

        var innerRadius = radius * 0.45;
        if (props.TryGetProperty("inner_radius", out var irVal) && irVal.TryCoerceToDouble(out var irNum))
        {
            innerRadius = Math.Max(0.5, irNum);
        }

        var thickness = Math.Max(1.0, radius * 0.25);
        if (props.TryGetProperty("thickness", out var thVal) && thVal.TryCoerceToDouble(out var thNum))
        {
            thickness = Math.Max(0.5, thNum);
        }

        var startX = width * 0.2;
        var startY = height * 0.5;
        var endX = width * 0.8;
        var endY = height * 0.5;
        if (props.TryGetProperty("start", out var stPtVal) && stPtVal.TryCoerceToPoint2D(out var stPt))
        {
            startX = stPt.X;
            startY = stPt.Y;
        }

        if (props.TryGetProperty("end", out var endPtVal) && endPtVal.TryCoerceToPoint2D(out var endPt))
        {
            endX = endPt.X;
            endY = endPt.Y;
        }

        var op = "union";
        if (props.TryGetProperty("op", out var opVal) || props.TryGetProperty("operation", out opVal))
        {
            op = opVal switch
            {
                GfxString s => s.Value.ToLowerInvariant(),
                GfxSymbol sym => sym.Identifier.ToLowerInvariant(),
                _ => "union"
            };
        }

        var k = 8.0;
        if ((props.TryGetProperty("smoothness", out var kVal) || props.TryGetProperty("k", out kVal)) && kVal.TryCoerceToDouble(out var kNum))
        {
            k = Math.Max(0.01, kNum);
        }

        var onion = 0.0;
        if (props.TryGetProperty("onion", out var onVal) && onVal.TryCoerceToDouble(out var onNum))
        {
            onion = Math.Max(0.0, onNum);
        }

        return new SdfPrimitive(
            shapeType,
            cx,
            cy,
            radius,
            innerRadius,
            halfW,
            halfH,
            cornerRadius,
            rotationRad,
            vertices,
            thickness,
            startX,
            startY,
            endX,
            endY,
            op,
            k,
            onion);
    }

    private sealed record SdfPrimitive(
        string ShapeType,
        double CenterX,
        double CenterY,
        double Radius,
        double InnerRadius,
        double HalfWidth,
        double HalfHeight,
        double CornerRadius,
        double RotationRad,
        int Vertices,
        double Thickness,
        double StartX,
        double StartY,
        double EndX,
        double EndY,
        string Operation,
        double Smoothness,
        double Onion);
}
