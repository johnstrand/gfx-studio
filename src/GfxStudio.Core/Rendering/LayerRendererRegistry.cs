using GfxStudio.Core.Rendering.Layers;
using GfxStudio.Core.Rendering.Layers.Shapes;

namespace GfxStudio.Core.Rendering;

/// <summary>Registry of available <see cref="ILayerRenderer"/> implementations keyed by layer type.</summary>
public sealed class LayerRendererRegistry
{
    private readonly Dictionary<string, ILayerRenderer> _renderers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ILayerRenderer> Renderers => _renderers;

    public IReadOnlyList<string> RegisteredLayerTypes =>
        _renderers.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    public static LayerRendererRegistry CreateDefault(IEnumerable<ILayerRenderer>? additionalRenderers = null)
    {
        var registry = new LayerRendererRegistry();
        registry.Register(new FillLayerRenderer());
        registry.Register(new FractalCloudsLayerRenderer());
        registry.Register(new TextLayerRenderer());
        registry.Register(new GradientLayerRenderer());
        registry.Register(new RectLayerRenderer());
        registry.Register(new CircleLayerRenderer());
        registry.Register(new CircleLayerRenderer("ellipse"));
        registry.Register(new LineLayerRenderer());
        registry.Register(new PolygonLayerRenderer());
        registry.Register(new PolygonLayerRenderer("star"));
        if (additionalRenderers is not null)
        {
            foreach (var renderer in additionalRenderers)
            {
                registry.Register(renderer);
            }
        }

        return registry;
    }

    public void Register(ILayerRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrWhiteSpace(renderer.LayerType);
        _renderers[renderer.LayerType] = renderer;
    }

    public bool TryGetRenderer(string layerType, out ILayerRenderer renderer) =>
        _renderers.TryGetValue(layerType, out renderer!);
}
