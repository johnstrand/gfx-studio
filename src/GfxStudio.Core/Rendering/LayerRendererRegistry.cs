using GfxStudio.Core.Rendering.Effects;
using GfxStudio.Core.Rendering.Layers;
using GfxStudio.Core.Rendering.Layers.Patterns;
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
        registry.Register(new FractalCloudsLayerRenderer("perlin", defaultOctaves: 1));
        registry.Register(new FractalCloudsLayerRenderer("simplex", defaultOctaves: 1));
        registry.Register(new WorleyLayerRenderer("worley"));
        registry.Register(new WorleyLayerRenderer("voronoi"));
        registry.Register(new CheckerboardLayerRenderer());
        registry.Register(new StripesLayerRenderer());
        registry.Register(new GridLayerRenderer());
        registry.Register(new DotsLayerRenderer());
        registry.Register(new BricksLayerRenderer());
        registry.Register(new TruchetLayerRenderer());
        registry.Register(new GeometricTilingLayerRenderer("tiling", "hex"));
        registry.Register(new GeometricTilingLayerRenderer("hex_grid", "hex"));
        registry.Register(new GeometricTilingLayerRenderer("chevron", "chevron"));
        registry.Register(new WavesLayerRenderer());
        registry.Register(new SpiralLayerRenderer());
        registry.Register(new WoodLayerRenderer());
        registry.Register(new MarbleLayerRenderer());
        registry.Register(new CausticsLayerRenderer());
        registry.Register(new FlowFieldLayerRenderer("flow_field"));
        registry.Register(new FlowFieldLayerRenderer("curl_noise"));
        registry.Register(new StarfieldLayerRenderer());
        registry.Register(new ReactionDiffusionLayerRenderer("reaction_diffusion"));
        registry.Register(new ReactionDiffusionLayerRenderer("turing"));
        registry.Register(new SdfLayerRenderer());
        registry.Register(new ImageLayerRenderer());
        registry.Register(new TextLayerRenderer());
        registry.Register(new GradientLayerRenderer());
        registry.Register(new RectLayerRenderer());
        registry.Register(new CircleLayerRenderer());
        registry.Register(new CircleLayerRenderer("ellipse"));
        registry.Register(new LineLayerRenderer());
        registry.Register(new PolygonLayerRenderer());
        registry.Register(new PolygonLayerRenderer("star"));
        foreach (var effect in LayerEffectRegistry.CreateDefault().Effects.Values)
        {
            registry.Register(new EffectAdjustmentLayerRenderer(effect));
        }

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
