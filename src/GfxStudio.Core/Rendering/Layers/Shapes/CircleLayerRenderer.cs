using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Shapes;

/// <summary>Renders a circle or ellipse with optional fill and outline.</summary>
public sealed class CircleLayerRenderer(string layerType = "circle") : ILayerRenderer
{
    public string LayerType { get; } = layerType;

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bitmap = context.CreateLayerBitmap();
        var fillColor = ShapeLayerHelpers.ResolveColor(context, "fill_color", "color");
        var hasFill = ShapeLayerHelpers.HasColor(context, "fill_color", "color");
        var strokeColor = ShapeLayerHelpers.ResolveColor(context, "stroke_color");
        var hasStroke = ShapeLayerHelpers.HasColor(context, "stroke_color");
        if (!hasFill && !hasStroke)
        {
            context.AddDiagnostic("RND036", $"Circle layer '{context.LayerId}' requires a fill color or stroke color.");
            return bitmap;
        }

        var center = ShapeLayerHelpers.ResolvePoint(context, "center", new Point2D(bitmap.Width / 2.0f, bitmap.Height / 2.0f));
        var defaultRadius = Math.Min(bitmap.Width, bitmap.Height) / 2.0;
        var radiusX = ShapeLayerHelpers.ResolveNumber(context, "radius", defaultRadius);
        var radiusY = radiusX;
        if (context.LayerProperties.TryGetProperty("radii", out var radiiValue))
        {
            if (radiiValue.TryCoerceToPoint2D(out var radii) && float.IsFinite(radii.X) && float.IsFinite(radii.Y))
            {
                radiusX = radii.X;
                radiusY = radii.Y;
            }
            else
            {
                context.AddDiagnostic("RND037", $"Property 'radii' on layer '{context.LayerId}' must contain finite x and y radii.");
            }
        }

        if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY) || radiusX <= 0.0 || radiusY <= 0.0)
        {
            context.AddDiagnostic("RND038", $"Circle radii on layer '{context.LayerId}' must be positive finite numbers.");
            return bitmap;
        }

        var strokeWidth = ShapeLayerHelpers.ResolveNumber(context, "stroke_width", 0.0);
        if (!ShapeLayerHelpers.ValidateStrokeWidth(context, strokeWidth))
        {
            return bitmap;
        }

        var oval = new SKRect(center.X - (float)radiusX, center.Y - (float)radiusY, center.X + (float)radiusX, center.Y + (float)radiusY);
        using var canvas = new SKCanvas(bitmap);
        if (fillColor is { } fill)
        {
            using var paint = new SKPaint { Color = fill.ToSKColor(), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawOval(oval, paint);
        }

        if (strokeColor is { } stroke && strokeWidth > 0.0)
        {
            using var paint = new SKPaint
            {
                Color = stroke.ToSKColor(),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)strokeWidth
            };
            canvas.DrawOval(oval, paint);
        }

        canvas.Flush();
        return bitmap;
    }
}
