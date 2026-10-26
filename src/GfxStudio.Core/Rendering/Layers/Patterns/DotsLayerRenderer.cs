using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders a procedural halftone / polka dot pattern (<c>type = dots</c>).
/// </summary>
public sealed class DotsLayerRenderer : ILayerRenderer
{
    public string LayerType => "dots";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["spacing", "cell_size", "radius", "color", "background", "pattern_offset", "phase_offset"];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var (spacingX, spacingY) = ResolveSpacing(context, defaultSpacing: 12.0);
        var radius = StripesLayerRenderer.ResolvePositiveDouble(context, "radius", null, defaultValue: 4.0);
        var (offsetX, offsetY) = StripesLayerRenderer.ResolvePatternOffset(context);
        var dotColor = CheckerboardLayerRenderer.ResolveColor(context, "color", ColorRgba.White);
        var background = CheckerboardLayerRenderer.ResolveColor(context, "background", new ColorRgba(0, 0, 0, 0));

        var width = bitmap.Width;
        var height = bitmap.Height;
        var radiusSq = radius * radius;
        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var sy = y - offsetY;
            var cellCenterY = (Math.Floor(sy / spacingY) + 0.5) * spacingY;
            var dy = (sy + 0.5) - cellCenterY;
            var dySq = dy * dy;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var sx = x - offsetX;
                var cellCenterX = (Math.Floor(sx / spacingX) + 0.5) * spacingX;
                var dx = (sx + 0.5) - cellCenterX;
                var distSq = (dx * dx) + dySq;
                var chosen = distSq <= radiusSq ? dotColor : background;

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), chosen);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    private static (double X, double Y) ResolveSpacing(LayerRenderContext context, double defaultSpacing)
    {
        if (context.LayerProperties.TryGetProperty("spacing", out var val)
            || context.LayerProperties.TryGetProperty("cell_size", out val))
        {
            if (val is GfxNumber num && num.Value > 0.0)
            {
                return (num.Value, num.Value);
            }

            if (val.TryCoerceToDimensions(out var dims))
            {
                return (dims.Width, dims.Height);
            }

            context.AddDiagnostic("RND014", $"Property 'spacing' on layer '{context.LayerId}' must be a positive number or dimensions.");
        }

        return (defaultSpacing, defaultSpacing);
    }
}
