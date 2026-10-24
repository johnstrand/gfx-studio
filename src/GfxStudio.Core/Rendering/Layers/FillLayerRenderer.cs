using GfxStudio.Core.Evaluation;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>Renders a solid color fill layer.</summary>
public sealed class FillLayerRenderer : ILayerRenderer
{
    public string LayerType => "fill";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        if (!context.LayerProperties.TryGetProperty("color", out var colorValue))
        {
            context.AddDiagnostic("RND011", $"Fill layer '{context.LayerId}' is missing required property 'color'.");
            return bitmap;
        }

        var color = colorValue switch
        {
            GfxColor gfxColor => gfxColor.Color,
            GfxColorRamp ramp => ramp.Ramp.Sample(0.0f),
            _ => (GfxStudio.Core.Primitives.ColorRgba?)null
        };

        if (color is null)
        {
            context.AddDiagnostic(
                "RND012",
                $"Property 'color' on fill layer '{context.LayerId}' must be a color or color ramp.");
            return bitmap;
        }

        bitmap.Erase(color.Value.ToSKColor());
        return bitmap;
    }
}
