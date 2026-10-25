using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>Renders text using SkiaSharp and a bundled fallback typeface.</summary>
public sealed class TextLayerRenderer : ILayerRenderer
{
    private static readonly GfxFontManager FontManager = new();

    public string LayerType => "text";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["text", "font_size", "color", "align", "font_family", "font_path", "stroke_color", "stroke_width", "font_weight", "font_style"];

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
        var weight = ResolveFontWeight(context);
        var slant = ResolveFontStyle(context);
        var fontStyle = new SKFontStyle((SKFontStyleWeight)weight, SKFontStyleWidth.Normal, slant);

        SKTypeface typeface;
        try
        {
            typeface = FontManager.LoadTypeface(fontPath, fontFamily, context.LayerSpan.FilePath, fontStyle);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            context.AddDiagnostic("RND016", $"Could not load text font: {exception.Message}");
            return bitmap;
        }

        using (typeface)
        using (var font = new SKFont(typeface, fontSize))
        {
            if (weight >= 600 && (int)typeface.FontStyle.Weight < 600)
            {
                font.Embolden = true;
            }

            if (slant is SKFontStyleSlant.Italic or SKFontStyleSlant.Oblique && typeface.FontStyle.Slant == SKFontStyleSlant.Upright)
            {
                font.SkewX = -0.25f;
            }

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

    private static int ResolveFontWeight(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("font_weight", out var value))
        {
            return (int)SKFontStyleWeight.Normal;
        }

        switch (value)
        {
            case GfxSymbol symbol:
                return ParseNamedWeight(symbol.Identifier, context);
            case GfxString str:
                return ParseNamedWeight(str.Value, context);
            case GfxNumber num when double.IsFinite(num.Value) && num.Value >= 100.0 && num.Value <= 900.0:
                return (int)Math.Round(num.Value);
            default:
                context.AddDiagnostic("RND023", $"Property 'font_weight' on text layer '{context.LayerId}' must be a valid weight name or a number between 100 and 900.");
                return (int)SKFontStyleWeight.Normal;
        }
    }

    private static int ParseNamedWeight(string name, LayerRenderContext context) =>
        name.ToLowerInvariant() switch
        {
            "thin" or "hairline" => 100,
            "extra_light" or "extralight" or "ultra_light" or "ultralight" => 200,
            "light" => 300,
            "normal" or "regular" => 400,
            "medium" => 500,
            "semi_bold" or "semibold" or "demi_bold" or "demibold" => 600,
            "bold" => 700,
            "extra_bold" or "extrabold" or "ultra_bold" or "ultrabold" => 800,
            "black" or "heavy" => 900,
            _ => ReportInvalidFontWeight(name, context)
        };

    private static int ReportInvalidFontWeight(string name, LayerRenderContext context)
    {
        context.AddDiagnostic("RND023", $"Unknown font weight '{name}' on text layer '{context.LayerId}'.");
        return (int)SKFontStyleWeight.Normal;
    }

    private static SKFontStyleSlant ResolveFontStyle(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("font_style", out var value))
        {
            return SKFontStyleSlant.Upright;
        }

        var name = value switch
        {
            GfxSymbol symbol => symbol.Identifier,
            GfxString str => str.Value,
            _ => null
        };

        if (name is null)
        {
            context.AddDiagnostic("RND024", $"Property 'font_style' on text layer '{context.LayerId}' must be normal, italic, or oblique.");
            return SKFontStyleSlant.Upright;
        }

        return name.ToLowerInvariant() switch
        {
            "normal" or "upright" => SKFontStyleSlant.Upright,
            "italic" => SKFontStyleSlant.Italic,
            "oblique" => SKFontStyleSlant.Oblique,
            _ => ReportInvalidFontStyle(name, context)
        };
    }

    private static SKFontStyleSlant ReportInvalidFontStyle(string name, LayerRenderContext context)
    {
        context.AddDiagnostic("RND024", $"Unknown font style '{name}' on text layer '{context.LayerId}'. Expected normal, italic, or oblique.");
        return SKFontStyleSlant.Upright;
    }
}
