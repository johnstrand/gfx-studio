using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
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
        List<GfxDiagnostic>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentNullException.ThrowIfNull(properties);

        LayerId = layerId;
        Properties = properties;
        Span = span.FilePath is null
            ? new SourceSpan("<memory>", new SourceLocation(1, 1, 0), new SourceLocation(1, 1, 0))
            : span;
        _diagnostics = diagnostics ?? [];
    }

    public string LayerId { get; }

    public GfxObject Properties { get; }

    public SourceSpan Span { get; }

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

    void Apply(SKBitmap target, EffectContext context);
}

/// <summary>Registry of available <see cref="ILayerEffect"/> implementations.</summary>
public sealed class LayerEffectRegistry
{
    private readonly Dictionary<string, ILayerEffect> _effects = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ILayerEffect> Effects => _effects;

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

    public ILayerEffect Effect => _effect;

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bitmap = context.CreateLayerBitmap();
        var effectContext = new EffectContext(context.LayerId, context.LayerProperties, context.LayerSpan);
        _effect.Apply(bitmap, effectContext);
        foreach (var diag in effectContext.Diagnostics)
        {
            context.AddDiagnostic(diag.Code, diag.Message, diag.Span);
        }

        return bitmap;
    }
}
