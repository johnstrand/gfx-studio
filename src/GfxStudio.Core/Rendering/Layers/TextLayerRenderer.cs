using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>Renders text using SkiaSharp and a bundled fallback typeface.</summary>
public sealed class TextLayerRenderer : ILayerRenderer
{
    private static readonly GfxFontManager FontManager = new();

    public string LayerType => "text";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bitmap = context.CreateLayerBitmap();

        if (!TryResolveText(context, out var text))
        {
            return bitmap;
        }

        var fontSize = ResolveFontSize(context);
        var color = ResolveColor(context);
        var alignment = ResolveAlignment(context);
        var fontFamily = ResolveOptionalString(context, "font_family");
        var fontPath = ResolveOptionalString(context, "font_path");

        SKTypeface typeface;
        try
        {
            typeface = FontManager.LoadTypeface(fontPath, fontFamily, context.LayerSpan.FilePath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            context.AddDiagnostic("RND016", $"Could not load text font: {exception.Message}");
            return bitmap;
        }

        using (typeface)
        using (var font = new SKFont(typeface, fontSize))
        using (var canvas = new SKCanvas(bitmap))
        using (var fillPaint = new SKPaint { Color = color.ToSKColor(), IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            var glyphs = new ushort[text.Length];
            font.GetGlyphs(text, glyphs);
            font.MeasureText(glyphs, out var bounds, fillPaint);

            var x = alignment.Horizontal switch
            {
                HorizontalAlign.Center => (context.LayerDimensions.Width - bounds.Width) / 2.0f - bounds.Left,
                HorizontalAlign.Right => context.LayerDimensions.Width - bounds.Right,
                _ => -bounds.Left
            };
            var y = alignment.Vertical switch
            {
                VerticalAlign.Center => (context.LayerDimensions.Height - bounds.Height) / 2.0f - bounds.Top,
                VerticalAlign.Bottom => context.LayerDimensions.Height - bounds.Bottom,
                _ => -bounds.Top
            };

            DrawStrokeIfRequested(context, canvas, font, text, x, y);
            canvas.DrawText(text, x, y, font, fillPaint);
            canvas.Flush();
        }

        return bitmap;
    }

    private static bool TryResolveText(LayerRenderContext context, out string text)
    {
        if (!context.LayerProperties.TryGetProperty("text", out var value))
        {
            context.AddDiagnostic("RND015", $"Text layer '{context.LayerId}' is missing required property 'text'.");
            text = string.Empty;
            return false;
        }

        switch (value)
        {
            case GfxString stringValue:
                text = stringValue.Value;
                return true;
            case GfxNumber numberValue:
                text = numberValue.ToDisplayString();
                return true;
            case GfxBoolean booleanValue:
                text = booleanValue.ToDisplayString();
                return true;
            default:
                context.AddDiagnostic("RND015", $"Property 'text' on layer '{context.LayerId}' must be a string, number, or boolean.");
                text = string.Empty;
                return false;
        }
    }

    private static float ResolveFontSize(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("font_size", out var value))
        {
            return 16.0f;
        }

        if (value is GfxNumber number && double.IsFinite(number.Value) && number.Value > 0.0 && number.Value <= float.MaxValue)
        {
            return (float)number.Value;
        }

        context.AddDiagnostic("RND017", $"Property 'font_size' on text layer '{context.LayerId}' must be a positive finite number.");
        return 16.0f;
    }

    private static ColorRgba ResolveColor(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("color", out var value))
        {
            return ColorRgba.Black;
        }

        if (value is GfxColor color)
        {
            return color.Color;
        }

        context.AddDiagnostic("RND018", $"Property 'color' on text layer '{context.LayerId}' must be a color.");
        return ColorRgba.Black;
    }

    private static Alignment ResolveAlignment(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("align", out var value))
        {
            return new Alignment(HorizontalAlign.Left, VerticalAlign.Top);
        }

        if (value.TryCoerceToAlignment(out var alignment))
        {
            return alignment;
        }

        context.AddDiagnostic("RND019", $"Property 'align' on text layer '{context.LayerId}' must contain valid horizontal and/or vertical alignment values.");
        return new Alignment(HorizontalAlign.Left, VerticalAlign.Top);
    }

    private static string? ResolveOptionalString(LayerRenderContext context, string propertyName)
    {
        if (!context.LayerProperties.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value is GfxString stringValue)
        {
            return stringValue.Value;
        }

        context.AddDiagnostic("RND020", $"Property '{propertyName}' on text layer '{context.LayerId}' must be a string.");
        return null;
    }

    private static void DrawStrokeIfRequested(
        LayerRenderContext context,
        SKCanvas canvas,
        SKFont font,
        string text,
        float x,
        float y)
    {
        var hasColor = context.LayerProperties.TryGetProperty("stroke_color", out var colorValue);
        var hasWidth = context.LayerProperties.TryGetProperty("stroke_width", out var widthValue);
        if (!hasColor && !hasWidth)
        {
            return;
        }

        var color = colorValue is GfxColor gfxColor ? gfxColor.Color : ColorRgba.Black;
        if (hasColor && colorValue is not GfxColor)
        {
            context.AddDiagnostic("RND021", $"Property 'stroke_color' on text layer '{context.LayerId}' must be a color.");
        }

        var width = widthValue is GfxNumber number && double.IsFinite(number.Value) && number.Value > 0.0
            ? (float)number.Value
            : 1.0f;
        if (hasWidth && (widthValue is not GfxNumber || !double.IsFinite(((GfxNumber)widthValue).Value) || ((GfxNumber)widthValue).Value <= 0.0))
        {
            context.AddDiagnostic("RND022", $"Property 'stroke_width' on text layer '{context.LayerId}' must be a positive finite number.");
        }

        using var strokePaint = new SKPaint
        {
            Color = color.ToSKColor(),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width
        };
        canvas.DrawText(text, x, y, font, strokePaint);
    }
}
