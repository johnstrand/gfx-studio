using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Shapes;

/// <summary>Renders a filled and/or stroked rectangle with optional corner radius and rotation.</summary>
public sealed class RectLayerRenderer : ILayerRenderer
{
    public string LayerType => "rect";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["fill_color", "color", "stroke_color", "stroke_width", "corner_radius", "rotation"];

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
            context.AddDiagnostic("RND034", $"Rectangle layer '{context.LayerId}' requires a fill color or stroke color.");
            return bitmap;
        }

        var strokeWidth = ShapeLayerHelpers.ResolveNumber(context, "stroke_width", 0.0);
        if (!ShapeLayerHelpers.ValidateStrokeWidth(context, strokeWidth))
        {
            return bitmap;
        }

        var radius = ShapeLayerHelpers.ResolveNumber(context, "corner_radius", 0.0);
        var rotation = ShapeLayerHelpers.ResolveNumber(context, "rotation", 0.0);
        if (radius < 0.0)
        {
            context.AddDiagnostic("RND035", $"Property 'corner_radius' on layer '{context.LayerId}' must be non-negative.");
            radius = 0.0;
        }

        var bounds = new SKRect(0, 0, bitmap.Width, bitmap.Height);
        radius = Math.Min(radius, Math.Min(bitmap.Width, bitmap.Height) / 2.0);
        using var canvas = new SKCanvas(bitmap);
        var saveCount = canvas.Save();
        canvas.Translate(bitmap.Width / 2.0f, bitmap.Height / 2.0f);
        canvas.RotateDegrees((float)rotation);
        canvas.Translate(-bitmap.Width / 2.0f, -bitmap.Height / 2.0f);

        if (fillColor is { } fill)
        {
            using var paint = new SKPaint { Color = fill.ToSKColor(), IsAntialias = true, Style = SKPaintStyle.Fill };
            if (radius > 0.0)
            {
                canvas.DrawRoundRect(bounds, (float)radius, (float)radius, paint);
            }
            else
            {
                canvas.DrawRect(bounds, paint);
            }
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
            if (radius > 0.0)
            {
                canvas.DrawRoundRect(bounds, (float)radius, (float)radius, paint);
            }
            else
            {
                canvas.DrawRect(bounds, paint);
            }
        }

        canvas.RestoreToCount(saveCount);
        canvas.Flush();
        return bitmap;
    }
}
