using SkiaSharp;

namespace GfxStudio.Core.Rendering;

/// <summary>Renders a single layer definition of a specific <see cref="LayerType"/> into an <see cref="SKBitmap"/>.</summary>
public interface ILayerRenderer
{
    /// <summary>Gets the .gfx layer type identifier (e.g., <c>"fill"</c>, <c>"fractal_clouds"</c>, <c>"text"</c>).</summary>
    string LayerType { get; }

    /// <summary>Renders the layer using the provided <paramref name="context"/>.</summary>
    SKBitmap RenderLayer(LayerRenderContext context);
}
