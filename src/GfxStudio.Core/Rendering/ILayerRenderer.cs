using SkiaSharp;

namespace GfxStudio.Core.Rendering;

/// <summary>Renders a single layer definition of a specific <see cref="LayerType"/> into an <see cref="SKBitmap"/>.</summary>
public interface ILayerRenderer
{
    /// <summary>Gets the .gfx layer type identifier (e.g., <c>"fill"</c>, <c>"fractal_clouds"</c>, <c>"text"</c>).</summary>
    string LayerType { get; }

    /// <summary>Gets the property names recognized by this layer renderer.</summary>
    IReadOnlyCollection<string> SupportedProperties => [];

    /// <summary>Renders the layer using the provided <paramref name="context"/>.</summary>
    SKBitmap RenderLayer(LayerRenderContext context);
}
