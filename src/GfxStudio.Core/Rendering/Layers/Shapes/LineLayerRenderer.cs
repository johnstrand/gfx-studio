using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Shapes;

/// <summary>Renders an anti-aliased line segment.</summary>
public sealed class LineLayerRenderer : ILayerRenderer
{
    public string LayerType => "line";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["color", "stroke_color", "start", "end", "width", "stroke_width"];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bitmap = context.CreateLayerBitmap();
        var color = ShapeLayerHelpers.ResolveColor(context, "color", "stroke_color");
        if (color is null)
        {
            context.AddDiagnostic("RND039", $"Line layer '{context.LayerId}' requires a color.");
            return bitmap;
        }

        var start = ShapeLayerHelpers.ResolvePoint(context, "start", new Point2D(0.0f, 0.0f));
        var end = ShapeLayerHelpers.ResolvePoint(context, "end", new Point2D(bitmap.Width, bitmap.Height));
        var width = ShapeLayerHelpers.ResolveNumber(context, "width", 1.0, "stroke_width");
        if (!double.IsFinite(width) || width <= 0.0)
        {
            context.AddDiagnostic("RND040", $"Line width on layer '{context.LayerId}' must be a positive finite number.");
            return bitmap;
        }

        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            Color = color.Value.ToSKColor(),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)width,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(start.X, start.Y, end.X, end.Y, paint);
        canvas.Flush();
        return bitmap;
    }
}
