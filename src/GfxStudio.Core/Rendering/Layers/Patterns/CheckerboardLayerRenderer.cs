using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders a crisp procedural checkerboard pattern (<c>type = checkerboard</c>).
/// </summary>
public sealed class CheckerboardLayerRenderer : ILayerRenderer
{
    public string LayerType => "checkerboard";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var (tileWidth, tileHeight) = ResolveTileSize(context, "tile_size", "cell_size", defaultSize: 8);
        var color1 = ResolveColor(context, "color1", ColorRgba.White);
        var color2 = ResolveColor(context, "color2", ColorRgba.Black);

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var ty = y / tileHeight;
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var tx = x / tileWidth;
                var chosen = ((tx + ty) & 1) == 0 ? color1 : color2;
                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), chosen);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    internal static (int Width, int Height) ResolveTileSize(
        LayerRenderContext context,
        string primaryName,
        string? aliasName,
        int defaultSize)
    {
        if (context.LayerProperties.TryGetProperty(primaryName, out var val)
            || (aliasName is not null && context.LayerProperties.TryGetProperty(aliasName, out val)))
        {
            if (val is GfxNumber num && num.Value >= 1.0)
            {
                var s = Math.Max(1, num.AsInt32());
                return (s, s);
            }

            if (val.TryCoerceToDimensions(out var dims))
            {
                return (Math.Max(1, dims.Width), Math.Max(1, dims.Height));
            }

            context.AddDiagnostic("RND014", $"Property '{primaryName}' on layer '{context.LayerId}' must be a positive integer or dimensions.");
        }

        return (defaultSize, defaultSize);
    }

    internal static ColorRgba ResolveColor(LayerRenderContext context, string propName, ColorRgba defaultValue)
    {
        if (!context.LayerProperties.TryGetProperty(propName, out var val))
        {
            return defaultValue;
        }

        if (val is GfxColor color)
        {
            return color.Color;
        }

        context.AddDiagnostic("RND013", $"Property '{propName}' on layer '{context.LayerId}' must be a color.");
        return defaultValue;
    }
}
