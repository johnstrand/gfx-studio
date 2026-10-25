using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Shapes;

/// <summary>Renders a polygon or star from explicit points or regular polygon parameters.</summary>
public sealed class PolygonLayerRenderer(string layerType = "polygon") : ILayerRenderer
{
    public string LayerType { get; } = layerType;

    public IReadOnlyCollection<string> SupportedProperties =>
        ["points", "sides", "radius", "center", "rotation", "inner_radius", "fill_color", "color", "stroke_color", "stroke_width"];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bitmap = context.CreateLayerBitmap();
        var points = ResolvePoints(context, bitmap.Width, bitmap.Height);
        if (points.Count < 3)
        {
            context.AddDiagnostic("RND041", $"Polygon layer '{context.LayerId}' requires at least three valid points.");
            return bitmap;
        }

        var fillColor = ShapeLayerHelpers.ResolveColor(context, "fill_color", "color");
        var hasFill = ShapeLayerHelpers.HasColor(context, "fill_color", "color");
        var strokeColor = ShapeLayerHelpers.ResolveColor(context, "stroke_color");
        var hasStroke = ShapeLayerHelpers.HasColor(context, "stroke_color");
        if (!hasFill && !hasStroke)
        {
            context.AddDiagnostic("RND042", $"Polygon layer '{context.LayerId}' requires a fill color or stroke color.");
            return bitmap;
        }

        var strokeWidth = ShapeLayerHelpers.ResolveNumber(context, "stroke_width", 0.0);
        if (!ShapeLayerHelpers.ValidateStrokeWidth(context, strokeWidth))
        {
            return bitmap;
        }

        using var path = new SKPath();
        path.MoveTo(points[0].X, points[0].Y);
        for (var index = 1; index < points.Count; index++)
        {
            path.LineTo(points[index].X, points[index].Y);
        }

        path.Close();
        using var canvas = new SKCanvas(bitmap);
        if (fillColor is { } fill)
        {
            using var paint = new SKPaint { Color = fill.ToSKColor(), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawPath(path, paint);
        }

        if (strokeColor is { } stroke && strokeWidth > 0.0)
        {
            using var paint = new SKPaint
            {
                Color = stroke.ToSKColor(),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)strokeWidth,
                StrokeJoin = SKStrokeJoin.Round
            };
            canvas.DrawPath(path, paint);
        }

        canvas.Flush();
        return bitmap;
    }

    private List<Point2D> ResolvePoints(LayerRenderContext context, int width, int height)
    {
        if (context.LayerProperties.TryGetProperty("points", out var pointsValue))
        {
            if (pointsValue is not GfxArray pointsArray)
            {
                context.AddDiagnostic("RND043", $"Property 'points' on layer '{context.LayerId}' must be an array of points.");
                return [];
            }

            var points = new List<Point2D>(pointsArray.Items.Count);
            foreach (var item in pointsArray.Items)
            {
                if (item.TryCoerceToPoint2D(out var point) && float.IsFinite(point.X) && float.IsFinite(point.Y))
                {
                    points.Add(point);
                }
                else
                {
                    context.AddDiagnostic("RND043", $"Each item in 'points' on layer '{context.LayerId}' must contain finite x and y values.");
                    return [];
                }
            }

            return points;
        }

        var isStar = string.Equals(LayerType, "star", StringComparison.OrdinalIgnoreCase);
        var sides = ShapeLayerHelpers.ResolveNumber(context, "sides", isStar ? 5.0 : 6.0);
        if (sides != Math.Truncate(sides) || sides < 3 || sides > 512)
        {
            context.AddDiagnostic("RND044", $"Property 'sides' on layer '{context.LayerId}' must be an integer from 3 to 512.");
            return [];
        }

        var radius = ShapeLayerHelpers.ResolveNumber(context, "radius", Math.Min(width, height) / 2.0);
        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            context.AddDiagnostic("RND045", $"Property 'radius' on layer '{context.LayerId}' must be positive.");
            return [];
        }

        var center = ShapeLayerHelpers.ResolvePoint(context, "center", new Point2D(width / 2.0f, height / 2.0f));
        var rotation = ShapeLayerHelpers.ResolveNumber(context, "rotation", 0.0);
        var innerRadius = ShapeLayerHelpers.ResolveNumber(context, "inner_radius", radius / 2.0);
        var hasInnerRadius = isStar || context.LayerProperties.Properties.ContainsKey("inner_radius");
        if (hasInnerRadius && (!double.IsFinite(innerRadius) || innerRadius <= 0.0 || innerRadius >= radius))
        {
            context.AddDiagnostic("RND046", $"Property 'inner_radius' on layer '{context.LayerId}' must be positive and smaller than radius.");
            return [];
        }

        var vertexCount = checked((int)sides * (hasInnerRadius ? 2 : 1));
        var generated = new List<Point2D>(vertexCount);
        for (var index = 0; index < vertexCount; index++)
        {
            var angle = (rotation - 90.0 + index * 360.0 / vertexCount) * Math.PI / 180.0;
            var vertexRadius = hasInnerRadius && index % 2 == 1 ? innerRadius : radius;
            generated.Add(new Point2D(
                center.X + (float)(Math.Cos(angle) * vertexRadius),
                center.Y + (float)(Math.Sin(angle) * vertexRadius)));
        }

        return generated;
    }
}
