using System.Runtime.InteropServices;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders a crisp procedural grid pattern (<c>type = grid</c>).
/// </summary>
public sealed class GridLayerRenderer : ILayerRenderer
{
    public string LayerType => "grid";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var (cellWidth, cellHeight) = CheckerboardLayerRenderer.ResolveTileSize(context, "cell_size", "tile_size", defaultSize: 16);
        var lineWidth = (int)Math.Max(1, Math.Round(StripesLayerRenderer.ResolvePositiveDouble(context, "line_width", "thickness", defaultValue: 1.0)));
        var lineColor = CheckerboardLayerRenderer.ResolveColor(context, "color", ColorRgba.White);
        var background = CheckerboardLayerRenderer.ResolveColor(context, "background", new ColorRgba(0, 0, 0, 0));

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var isHorizontalLine = (y % cellHeight) < lineWidth;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var isLine = isHorizontalLine || (x % cellWidth) < lineWidth;
                var chosen = isLine ? lineColor : background;
                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), chosen);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }
}
