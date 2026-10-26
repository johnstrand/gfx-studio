using SkiaSharp;

namespace GfxStudio.Core.Rendering;

/// <summary>Describes a single property accepted by a layer renderer, effect, or universal layer block.</summary>
public sealed record LayerPropertyDescriptor(
    string Name,
    string ValueType,
    bool IsRequired = false,
    string? DefaultValue = null,
    string? Description = null,
    IReadOnlyList<string>? AllowedValues = null,
    IReadOnlyList<string>? Aliases = null);

/// <summary>Renders a single layer definition of a specific <see cref="LayerType"/> into an <see cref="SKBitmap"/>.</summary>
public interface ILayerRenderer
{
    /// <summary>Gets the .gfx layer type identifier (e.g., <c>"fill"</c>, <c>"fractal_clouds"</c>, <c>"text"</c>).</summary>
    string LayerType { get; }

    /// <summary>Gets the high-level category for this layer type.</summary>
    string Category => LayerMetadataCatalog.GetLayerCategory(LayerType);

    /// <summary>Gets a human-readable summary description of this layer type.</summary>
    string Description => LayerMetadataCatalog.GetLayerDescription(LayerType);

    /// <summary>Gets a self-contained example .gfx snippet demonstrating this layer type.</summary>
    string ExampleGfx => LayerMetadataCatalog.GetLayerExample(LayerType);

    /// <summary>Gets the property names recognized by this layer renderer.</summary>
    IReadOnlyCollection<string> SupportedProperties => [];

    /// <summary>Gets structured metadata descriptors for this layer's properties.</summary>
    IReadOnlyList<LayerPropertyDescriptor> PropertyDescriptors =>
        LayerMetadataCatalog.GetLayerPropertyDescriptors(LayerType, SupportedProperties);

    /// <summary>Renders the layer using the provided <paramref name="context"/>.</summary>
    SKBitmap RenderLayer(LayerRenderContext context);
}
