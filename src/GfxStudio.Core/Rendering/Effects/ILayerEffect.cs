using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Randomness;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Effects;

/// <summary>Execution context passed to an <see cref="ILayerEffect"/> when transforming a bitmap.</summary>
public sealed class EffectContext
{
    private readonly List<GfxDiagnostic> _diagnostics;

    public EffectContext(
        string layerId,
        GfxObject properties,
        SourceSpan span = default,
        List<GfxDiagnostic>? diagnostics = null,
        IGfxRandom? random = null,
        Func<string, SKBitmap?>? resolveLayerBitmap = null,
        EvaluationContext? evaluationContext = null,
        SKBitmap? sampleSource = null,
        SKBitmap? canvasBitmap = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentNullException.ThrowIfNull(properties);

        LayerId = layerId;
        Properties = properties;
        Span = span.FilePath is null
            ? new SourceSpan("<memory>", new SourceLocation(1, 1, 0), new SourceLocation(1, 1, 0))
            : span;
        _diagnostics = diagnostics ?? [];
        Random = random ?? new GfxRandom(0);
        ResolveLayerBitmap = resolveLayerBitmap;
        EvaluationContext = evaluationContext;
        SampleSource = sampleSource;
        CanvasBitmap = canvasBitmap ?? sampleSource;
    }

    public string LayerId { get; }

    public GfxObject Properties { get; }

    public SourceSpan Span { get; }

    public IGfxRandom Random { get; }

    public Func<string, SKBitmap?>? ResolveLayerBitmap { get; }

    public EvaluationContext? EvaluationContext { get; }

    public SKBitmap? SampleSource { get; }

    public SKBitmap? CanvasBitmap { get; }

    public IReadOnlyList<GfxDiagnostic> Diagnostics => _diagnostics;

    public void AddDiagnostic(string code, string message)
    {
        _diagnostics.Add(new GfxDiagnostic(code, message, Span, DiagnosticSeverity.Error));
    }
}

/// <summary>Represents a filter, adjustment, or stylization effect that modifies an <see cref="SKBitmap"/> in place.</summary>
public interface ILayerEffect
{
    string EffectType { get; }

    string Category => LayerMetadataCatalog.GetEffectCategory(EffectType);

    string Description => LayerMetadataCatalog.GetEffectDescription(EffectType);

    string ExampleGfx => LayerMetadataCatalog.GetEffectExample(EffectType);

    IReadOnlyCollection<string> SupportedProperties => [];

    IReadOnlyList<LayerPropertyDescriptor> PropertyDescriptors =>
        LayerMetadataCatalog.GetEffectPropertyDescriptors(EffectType, SupportedProperties);

    void Apply(SKBitmap target, EffectContext context);
}

/// <summary>Registry of available <see cref="ILayerEffect"/> implementations.</summary>
public sealed class LayerEffectRegistry
{
    private readonly Dictionary<string, ILayerEffect> _effects = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ILayerEffect> Effects => _effects;

    public IReadOnlyList<string> RegisteredEffectNames =>
        _effects.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    public static LayerEffectRegistry CreateDefault()
    {
        var registry = new LayerEffectRegistry();
        registry.Register(new GaussianBlurEffect("gaussian_blur"));
        registry.Register(new GaussianBlurEffect("box_blur"));
        registry.Register(new GaussianBlurEffect("blur"));
        registry.Register(new ThresholdEffect());
        registry.Register(new PosterizeEffect());
        registry.Register(new PixelateEffect());
        registry.Register(new ColorAdjustEffect("color_adjust"));
        registry.Register(new ColorAdjustEffect("invert", defaultInvert: true));
        registry.Register(new OutlineEffect());
        registry.Register(new DropShadowEffect("drop_shadow"));
        registry.Register(new DropShadowEffect("shadow"));

        // Spatial distortion & displacement effects (#24)
        registry.Register(new WarpEffect("warp"));
        registry.Register(new WarpEffect("displace"));
        registry.Register(new TwirlEffect("twirl"));
        registry.Register(new TwirlEffect("swirl"));
        registry.Register(new RippleEffect());
        registry.Register(new SpherizeEffect("spherize", 0.6));
        registry.Register(new SpherizeEffect("pinch", -0.6));
        registry.Register(new KaleidoscopeEffect());

        // Surface, print & post-processing filters (#25)
        registry.Register(new EmbossEffect());
        registry.Register(new BevelEffect());
        registry.Register(new NormalMapEffect());
        registry.Register(new HalftoneEffect());
        registry.Register(new DitherStippleEffect("dither", "bayer"));
        registry.Register(new DitherStippleEffect("stipple", "stipple"));
        registry.Register(new EdgeDetectEffect("edge_detect"));
        registry.Register(new EdgeDetectEffect("sobel"));
        registry.Register(new VignetteEffect());
        registry.Register(new BloomEffect("bloom"));
        registry.Register(new BloomEffect("glow"));
        registry.Register(new ChromaticAberrationEffect());
        registry.Register(new CrtScanlinesEffect("crt"));
        registry.Register(new CrtScanlinesEffect("scanlines"));
        registry.Register(new KuwaharaEffect("oil_paint"));
        registry.Register(new KuwaharaEffect("kuwahara"));

        // Programmable per-pixel shader engine (#26)
        registry.Register(new PixelShaderEffect("shader"));
        registry.Register(new PixelShaderEffect("pixel_shader"));
        return registry;
    }

    public void Register(ILayerEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentException.ThrowIfNullOrWhiteSpace(effect.EffectType);
        _effects[effect.EffectType] = effect;
    }

    public bool TryGetEffect(string effectType, out ILayerEffect effect) =>
        _effects.TryGetValue(effectType, out effect!);

    public bool TryGet(string effectType, out ILayerEffect effect) =>
        _effects.TryGetValue(effectType, out effect!);
}

/// <summary>
/// Adapter that exposes an <see cref="ILayerEffect"/> in <see cref="LayerRendererRegistry"/> for standalone adjustment layers.
/// </summary>
public sealed class EffectAdjustmentLayerRenderer : ILayerRenderer
{
    private readonly ILayerEffect _effect;

    public EffectAdjustmentLayerRenderer(ILayerEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effect = effect;
    }

    public string LayerType => _effect.EffectType;

    public string Category => _effect.Category;

    public string Description => _effect.Description;

    public string ExampleGfx => _effect.ExampleGfx;

    public IReadOnlyCollection<string> SupportedProperties => _effect.SupportedProperties;

    public IReadOnlyList<LayerPropertyDescriptor> PropertyDescriptors => _effect.PropertyDescriptors;

    public ILayerEffect Effect => _effect;

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bitmap = context.CreateLayerBitmap();
        var effectContext = new EffectContext(
            context.LayerId,
            context.LayerProperties,
            context.LayerSpan,
            random: context.Random,
            evaluationContext: context.EvaluationContext);
        _effect.Apply(bitmap, effectContext);
        foreach (var diag in effectContext.Diagnostics)
        {
            context.AddDiagnostic(diag.Code, diag.Message, diag.Span);
        }

        return bitmap;
    }
}
