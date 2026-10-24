using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness;
using SkiaSharp;

namespace GfxStudio.Core.Rendering;

/// <summary>Provides execution state, evaluated properties, dimensions, and deterministic randomness for rendering a single layer.</summary>
public sealed class LayerRenderContext
{
    private readonly List<GfxDiagnostic> _diagnostics;

    public LayerRenderContext(
        string layerId,
        GfxObject layerProperties,
        Dimensions canvasDimensions,
        Dimensions layerDimensions,
        GfxRandom random,
        EvaluationContext evaluationContext,
        SourceSpan layerSpan = default,
        List<GfxDiagnostic>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentNullException.ThrowIfNull(layerProperties);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(evaluationContext);

        LayerId = layerId;
        LayerProperties = layerProperties;
        CanvasDimensions = canvasDimensions;
        LayerDimensions = layerDimensions;
        Random = random;
        EvaluationContext = evaluationContext;
        LayerSpan = layerSpan.FilePath is null
            ? new SourceSpan("<memory>", new SourceLocation(1, 1, 0), new SourceLocation(1, 1, 0))
            : layerSpan;
        _diagnostics = diagnostics ?? [];
    }

    public string LayerId { get; }

    public GfxObject LayerProperties { get; }

    public Dimensions CanvasDimensions { get; }

    public Dimensions LayerDimensions { get; }

    public GfxRandom Random { get; }

    public EvaluationContext EvaluationContext { get; }

    public SourceSpan LayerSpan { get; }

    public IReadOnlyList<GfxDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Allocates a new <see cref="SKBitmap"/> sized to <see cref="LayerDimensions"/> initialized to transparent black.</summary>
    public SKBitmap CreateLayerBitmap()
    {
        var bitmap = new SKBitmap(new SKImageInfo(
            LayerDimensions.Width,
            LayerDimensions.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    /// <summary>Records a layer-rendering diagnostic error.</summary>
    public void AddDiagnostic(string code, string message, SourceSpan? span = null)
    {
        _diagnostics.Add(new GfxDiagnostic(code, message, span ?? LayerSpan, DiagnosticSeverity.Error));
    }
}
