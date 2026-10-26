using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Parsing.Ast;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness;
using GfxStudio.Core.Rendering.Effects;
using SkiaSharp;

namespace GfxStudio.Core.Rendering;

/// <summary>Result of rendering and compositing a .gfx canvas.</summary>
public sealed record CanvasRenderResult(
    SKBitmap? Bitmap,
    Dimensions? Dimensions,
    IReadOnlyList<GfxDiagnostic> Diagnostics) : IDisposable
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Dispose()
    {
        Bitmap?.Dispose();
    }
}

/// <summary>
/// Orchestrates layer evaluation in declaration order and composites rendered layers onto an <see cref="SKBitmap"/> canvas.
/// </summary>
public sealed class GfxCanvasCompositor
{
    private static readonly HashSet<string> UniversalLayerProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "type",
        "size",
        "offset",
        "position",
        "mix",
        "opacity",
        "visible",
        "enabled",
        "seed",
        "mask",
        "copy",
        "transform",
        "rotation",
        "rotate",
        "shear",
        "scale",
        "transform_scale",
        "flip_x",
        "flip_y",
        "anchor",
        "pivot",
        "resample",
        "wrap",
        "wrap_mode",
        "effects"
    };

    private static readonly HashSet<string> TransformBlockProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "offset",
        "position",
        "rotation",
        "rotate",
        "scale",
        "transform_scale",
        "shear",
        "flip_x",
        "flip_y",
        "anchor",
        "pivot",
        "wrap",
        "wrap_mode",
        "resample"
    };

    public static IReadOnlyList<LayerPropertyDescriptor> UniversalPropertyDescriptors =>
        LayerMetadataCatalog.UniversalProperties;

    public static IReadOnlyList<LayerPropertyDescriptor> TransformPropertyDescriptors =>
        LayerMetadataCatalog.TransformProperties;

    private readonly LayerRendererRegistry _registry;
    private readonly LayerEffectRegistry _effectRegistry;
    private readonly HashSet<string> _allEffectPropertyNames;

    public GfxCanvasCompositor(LayerRendererRegistry? registry = null, LayerEffectRegistry? effectRegistry = null)
    {
        _registry = registry ?? LayerRendererRegistry.CreateDefault();
        _effectRegistry = effectRegistry ?? LayerEffectRegistry.CreateDefault();
        _allEffectPropertyNames = new HashSet<string>(
            InlineEffectBindings.Select(b => b.PropName).Concat(_effectRegistry.Effects.Keys),
            StringComparer.OrdinalIgnoreCase);
    }

    public LayerRendererRegistry Registry => _registry;

    /// <summary>Parses, evaluates, and renders the given .gfx source into a composited <see cref="SKBitmap"/>.</summary>
    public CanvasRenderResult RenderSource(
        string source,
        ulong? masterSeed = null,
        RunContext? run = null,
        FrameContext? frame = null,
        IEnumerable<IGfxCallable>? functions = null,
        string filePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(source);
        var parseResult = GfxParser.Parse(source, filePath);
        if (parseResult.HasErrors || parseResult.Document is null)
        {
            return new CanvasRenderResult(null, null, parseResult.Diagnostics);
        }

        var effectiveMasterSeed = masterSeed ?? unchecked((ulong)DateTime.UtcNow.Ticks);
        var seedProvider = new DeterministicSeedProvider(effectiveMasterSeed);
        var effectiveRun = run ?? new RunContext(0, 1, seedProvider.DeriveSeed(0, 0, "__run__"));
        var effectiveFrame = frame ?? new FrameContext(0, 1, 15.0);

        var context = new EvaluationContext(
            parseResult.Document,
            effectiveRun,
            effectiveFrame,
            seedProvider.CreateLayerRandom(effectiveRun.Number, "__root__"),
            functions,
            frameRandom: seedProvider.CreateRandom(effectiveRun.Number, effectiveFrame.Number, "__root__:frame"));

        return RenderDocument(context, seedProvider);
    }

    /// <summary>Evaluates and composites all layers in <paramref name="context"/> onto a canvas.</summary>
    public CanvasRenderResult RenderDocument(
        EvaluationContext context,
        DeterministicSeedProvider? seedProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var effectiveSeedProvider = seedProvider ?? new DeterministicSeedProvider(context.Run.Seed);
        var diagnostics = new List<GfxDiagnostic>();
        var documentSpan = context.Document?.Span
            ?? new SourceSpan("<memory>", new SourceLocation(1, 1, 0), new SourceLocation(1, 1, 0));

        var evaluator = new GfxEvaluator(context, effectiveSeedProvider);

        // 1. Resolve output.dimensions (or default if only layers are tested with explicit dimensions)
        var canvasDimensions = ResolveCanvasDimensions(evaluator, diagnostics, documentSpan);
        if (canvasDimensions is null)
        {
            return new CanvasRenderResult(null, null, diagnostics.AsReadOnly());
        }

        context.OutputDimensions = canvasDimensions.Value;

        var canvas = new SKBitmap(new SKImageInfo(
            canvasDimensions.Value.Width,
            canvasDimensions.Value.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        canvas.Erase(SKColors.Transparent);

        var renderedLayers = new Dictionary<string, (SKBitmap Bitmap, Point2D Offset)>(StringComparer.Ordinal);
        var compositedLayers = new HashSet<string>(StringComparer.Ordinal);
        var samplingStack = new HashSet<string>(StringComparer.Ordinal);
        var declaredLayerOrder = ExtractDeclaredLayerOrder(context.Document);
        var layerSpans = ExtractLayerSpans(context.Document);

        GfxObject? EvaluateAndMergeLayerProps(string targetLayerId, HashSet<string> copyVisited)
        {
            if (!copyVisited.Add(targetLayerId))
            {
                return null;
            }

            var evalRes = evaluator.EvaluatePath($"layers.{targetLayerId}");
            foreach (var d in evalRes.Diagnostics)
            {
                if (!diagnostics.Contains(d))
                {
                    diagnostics.Add(d);
                }
            }

            if (evalRes.HasErrors || evalRes.Value is not GfxObject ownProps)
            {
                return null;
            }

            if (!ownProps.TryGetProperty("copy", out var copyVal))
            {
                return ownProps;
            }

            var sourceId = copyVal switch
            {
                GfxSymbol s => s.Identifier,
                GfxString str => str.Value,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return null;
            }

            var baseProps = EvaluateAndMergeLayerProps(sourceId, copyVisited);
            if (baseProps is null)
            {
                return null;
            }

            var merged = new Dictionary<string, GfxValue>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (var k in baseProps.OrderedKeys)
            {
                if (baseProps.Properties.TryGetValue(k, out var v))
                {
                    merged[k] = v;
                    ordered.Add(k);
                }
            }

            foreach (var k in ownProps.OrderedKeys)
            {
                if (string.Equals(k, "copy", StringComparison.OrdinalIgnoreCase)
                    || !ownProps.Properties.TryGetValue(k, out var v))
                {
                    continue;
                }

                if (!merged.ContainsKey(k))
                {
                    ordered.Add(k);
                }

                merged[k] = v;
            }

            return new GfxObject(merged, ordered.AsReadOnly());
        }

        var emptyLayersFallback = new GfxObject(new Dictionary<string, GfxValue>(StringComparer.Ordinal), []);

        context.ResolveLayerBitmap = (targetLayerId, _) =>
        {
            if (string.IsNullOrWhiteSpace(targetLayerId)
                || string.Equals(targetLayerId, context.CurrentLayerId, StringComparison.Ordinal)
                || !samplingStack.Add(targetLayerId))
            {
                return null;
            }

            try
            {
                if (renderedLayers.TryGetValue(targetLayerId, out var existing))
                {
                    return existing.Bitmap;
                }

                if (!declaredLayerOrder.Contains(targetLayerId, StringComparer.Ordinal))
                {
                    return null;
                }

                var props = EvaluateAndMergeLayerProps(targetLayerId, new HashSet<string>(StringComparer.Ordinal));
                if (props is null)
                {
                    return null;
                }

                return RenderSingleLayerWithTransforms(
                    targetLayerId,
                    props,
                    emptyLayersFallback,
                    canvas,
                    canvasDimensions.Value,
                    context,
                    effectiveSeedProvider,
                    layerSpans,
                    documentSpan,
                    diagnostics,
                    renderedLayers,
                    out Point2D _,
                    out BlendMode _,
                    out float _,
                    out bool _);
            }
            finally
            {
                samplingStack.Remove(targetLayerId);
            }
        };

        context.ResolveCanvasBitmap = (activeLayerId, _) =>
        {
            foreach (var prevLayerId in declaredLayerOrder)
            {
                if (string.Equals(prevLayerId, activeLayerId, StringComparison.Ordinal))
                {
                    break;
                }

                if (compositedLayerIdsContains(compositedLayers, prevLayerId) || samplingStack.Contains(prevLayerId))
                {
                    continue;
                }

                if (!samplingStack.Add(prevLayerId))
                {
                    continue;
                }

                try
                {
                    var props = EvaluateAndMergeLayerProps(prevLayerId, new HashSet<string>(StringComparer.Ordinal));
                    if (props is null)
                    {
                        compositedLayers.Add(prevLayerId);
                        continue;
                    }

                    var prevSpan = layerSpans.GetValueOrDefault(prevLayerId, documentSpan);
                    if (!IsLayerVisible(props, prevLayerId, prevSpan, diagnostics))
                    {
                        compositedLayers.Add(prevLayerId);
                        continue;
                    }

                    var bmp = RenderSingleLayerWithTransforms(
                        prevLayerId,
                        props,
                        emptyLayersFallback,
                        canvas,
                        canvasDimensions.Value,
                        context,
                        effectiveSeedProvider,
                        layerSpans,
                        documentSpan,
                        diagnostics,
                        renderedLayers,
                        out var offset,
                        out var blendMode,
                        out var opacity,
                        out var isAdjustment);

                    if (bmp is not null)
                    {
                        if (isAdjustment && blendMode == BlendMode.Normal && opacity >= 0.9999f)
                        {
                            WriteRegionInPlace(canvas, bmp, offset);
                        }
                        else
                        {
                            PixelBlender.Composite(canvas, bmp, offset, blendMode, opacity);
                        }
                    }

                    compositedLayers.Add(prevLayerId);
                }
                finally
                {
                    samplingStack.Remove(prevLayerId);
                }
            }

            return canvas;

            static bool compositedLayerIdsContains(HashSet<string> set, string id) => set.Contains(id);
        };

        // 2. Evaluate the layers block
        var layersResult = evaluator.EvaluatePath("layers");
        foreach (var d in layersResult.Diagnostics)
        {
            if (!diagnostics.Contains(d))
            {
                diagnostics.Add(d);
            }
        }

        if (layersResult.HasErrors || layersResult.Value is null)
        {
            foreach (var (cachedBitmap, _) in renderedLayers.Values)
            {
                cachedBitmap.Dispose();
            }

            canvas.Dispose();
            if (!layersResult.HasErrors && diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
            {
                diagnostics.Add(new GfxDiagnostic("RND001", "Missing required top-level 'layers' block.", documentSpan, DiagnosticSeverity.Error));
            }

            return new CanvasRenderResult(null, canvasDimensions, diagnostics.AsReadOnly());
        }

        if (layersResult.Value is not GfxObject layersObject)
        {
            foreach (var (cachedBitmap, _) in renderedLayers.Values)
            {
                cachedBitmap.Dispose();
            }

            canvas.Dispose();
            diagnostics.Add(new GfxDiagnostic("RND001", "Top-level 'layers' property must be an object block.", documentSpan, DiagnosticSeverity.Error));
            return new CanvasRenderResult(null, canvasDimensions, diagnostics.AsReadOnly());
        }

        return CompositeLayersCore(
            layersObject,
            canvasDimensions.Value,
            context,
            effectiveSeedProvider,
            diagnostics,
            canvas,
            renderedLayers,
            compositedLayers);
    }

    /// <summary>Composites an already-evaluated <c>layers</c> object onto a canvas of <paramref name="canvasDimensions"/>.</summary>
    public CanvasRenderResult CompositeLayers(
        GfxObject layersObject,
        Dimensions canvasDimensions,
        EvaluationContext context,
        DeterministicSeedProvider? seedProvider = null,
        List<GfxDiagnostic>? existingDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(layersObject);
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = existingDiagnostics ?? [];
        var effectiveSeedProvider = seedProvider ?? new DeterministicSeedProvider(context.Run.Seed);
        var canvas = new SKBitmap(new SKImageInfo(
            canvasDimensions.Width,
            canvasDimensions.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        canvas.Erase(SKColors.Transparent);

        var renderedLayers = new Dictionary<string, (SKBitmap Bitmap, Point2D Offset)>(StringComparer.Ordinal);
        var compositedLayers = new HashSet<string>(StringComparer.Ordinal);

        return CompositeLayersCore(
            layersObject,
            canvasDimensions,
            context,
            effectiveSeedProvider,
            diagnostics,
            canvas,
            renderedLayers,
            compositedLayers);
    }

    private CanvasRenderResult CompositeLayersCore(
        GfxObject layersObject,
        Dimensions canvasDimensions,
        EvaluationContext context,
        DeterministicSeedProvider effectiveSeedProvider,
        List<GfxDiagnostic> diagnostics,
        SKBitmap canvas,
        Dictionary<string, (SKBitmap Bitmap, Point2D Offset)> renderedLayers,
        HashSet<string> compositedLayers)
    {
        var layerSpans = ExtractLayerSpans(context.Document);
        var layerAstMap = ExtractLayerAstInfo(context.Document);
        var defaultSpan = context.Document?.Span
            ?? new SourceSpan("<memory>", new SourceLocation(1, 1, 0), new SourceLocation(1, 1, 0));

        var resolvedLayerProperties = new Dictionary<string, GfxObject>(StringComparer.Ordinal);
        var invalidLayerCopies = new HashSet<string>(StringComparer.Ordinal);
        var resolvingLayerCopies = new HashSet<string>(StringComparer.Ordinal);
        var layerCopyStack = new List<string>();

        bool TryResolveLayerProperties(string layerId, out GfxObject resolved)
        {
            if (resolvedLayerProperties.TryGetValue(layerId, out resolved!)) return true;
            if (invalidLayerCopies.Contains(layerId))
            {
                resolved = null!;
                return false;
            }

            if (!layersObject.Properties.TryGetValue(layerId, out var value) || value is not GfxObject ownProperties)
            {
                resolved = null!;
                return false;
            }

            if (!resolvingLayerCopies.Add(layerId))
            {
                var cycle = string.Join(" -> ", layerCopyStack.Append(layerId));
                diagnostics.Add(new GfxDiagnostic(
                    "RND055",
                    $"Layer copy cycle detected: {cycle}.",
                    layerSpans.GetValueOrDefault(layerId, defaultSpan),
                    DiagnosticSeverity.Error));
                invalidLayerCopies.Add(layerId);
                resolved = null!;
                return false;
            }

            layerCopyStack.Add(layerId);
            try
            {
                var mergedProperties = new Dictionary<string, GfxValue>(StringComparer.Ordinal);
                var orderedKeys = new List<string>();

                if (ownProperties.TryGetProperty("copy", out var copyValue))
                {
                    var sourceLayerId = copyValue switch
                    {
                        GfxSymbol symbol => symbol.Identifier,
                        GfxString text => text.Value,
                        _ => null
                    };

                    if (string.IsNullOrWhiteSpace(sourceLayerId)
                        || !layersObject.Properties.TryGetValue(sourceLayerId, out var sourceValue)
                        || sourceValue is not GfxObject)
                    {
                        diagnostics.Add(new GfxDiagnostic(
                            "RND054",
                            $"Property 'copy' on layer '{layerId}' must reference an existing layer by name.",
                            layerAstMap.GetValueOrDefault(layerId)?.PropertySpans.GetValueOrDefault("copy")
                                ?? layerSpans.GetValueOrDefault(layerId, defaultSpan),
                            DiagnosticSeverity.Error));
                        invalidLayerCopies.Add(layerId);
                        resolved = null!;
                        return false;
                    }

                    if (!TryResolveLayerProperties(sourceLayerId, out var sourceProperties))
                    {
                        invalidLayerCopies.Add(layerId);
                        resolved = null!;
                        return false;
                    }

                    foreach (var sourceKey in sourceProperties.OrderedKeys)
                    {
                        if (sourceProperties.Properties.TryGetValue(sourceKey, out var sourceProperty))
                        {
                            mergedProperties[sourceKey] = sourceProperty;
                            orderedKeys.Add(sourceKey);
                        }
                    }
                }

                foreach (var ownKey in ownProperties.OrderedKeys)
                {
                    if (string.Equals(ownKey, "copy", StringComparison.OrdinalIgnoreCase)
                        || !ownProperties.Properties.TryGetValue(ownKey, out var ownValue))
                    {
                        continue;
                    }

                    if (!mergedProperties.ContainsKey(ownKey))
                    {
                        orderedKeys.Add(ownKey);
                    }

                    mergedProperties[ownKey] = ownValue;
                }

                resolved = new GfxObject(mergedProperties, orderedKeys.AsReadOnly());
                resolvedLayerProperties[layerId] = resolved;
                return true;
            }
            finally
            {
                layerCopyStack.RemoveAt(layerCopyStack.Count - 1);
                resolvingLayerCopies.Remove(layerId);
            }
        }

        foreach (var layerId in layersObject.OrderedKeys)
        {
            if (layersObject.Properties.TryGetValue(layerId, out var layerValue) && layerValue is GfxObject)
            {
                TryResolveLayerProperties(layerId, out _);
            }
        }

        try
        {
            foreach (var layerId in layersObject.OrderedKeys)
            {
                if (!layersObject.Properties.TryGetValue(layerId, out var layerValue))
                {
                    continue;
                }

                var layerSpan = layerSpans.GetValueOrDefault(layerId, defaultSpan);
                if (layerValue is not GfxObject)
                {
                    diagnostics.Add(new GfxDiagnostic(
                        "RND002",
                        $"Layer '{layerId}' must be an object block.",
                        layerSpan,
                        DiagnosticSeverity.Error));
                    continue;
                }

                if (!resolvedLayerProperties.TryGetValue(layerId, out var layerProps))
                {
                    continue;
                }

                ValidateLayerProperties(layerId, layerProps, layerSpan, layerAstMap.GetValueOrDefault(layerId), diagnostics);

                if (compositedLayers.Contains(layerId))
                {
                    continue;
                }

                // Check visible / enabled flag (defaults to true)
                if (!IsLayerVisible(layerProps, layerId, layerSpan, diagnostics))
                {
                    compositedLayers.Add(layerId);
                    continue;
                }

                var layerBitmap = RenderSingleLayerWithTransforms(
                    layerId,
                    layerProps,
                    layersObject,
                    canvas,
                    canvasDimensions,
                    context,
                    effectiveSeedProvider,
                    layerSpans,
                    defaultSpan,
                    diagnostics,
                    renderedLayers,
                    out var offset,
                    out var blendMode,
                    out var opacity,
                    out var isAdjustmentLayer);

                if (layerBitmap is not null)
                {
                    if (isAdjustmentLayer && blendMode == BlendMode.Normal && opacity >= 0.9999f)
                    {
                        WriteRegionInPlace(canvas, layerBitmap, offset);
                    }
                    else
                    {
                        PixelBlender.Composite(canvas, layerBitmap, offset, blendMode, opacity);
                    }
                }

                compositedLayers.Add(layerId);
            }
        }
        finally
        {
            foreach (var (cachedBitmap, _) in renderedLayers.Values)
            {
                cachedBitmap.Dispose();
            }
        }

        return new CanvasRenderResult(canvas, canvasDimensions, diagnostics.AsReadOnly());
    }

    private SKBitmap? RenderSingleLayerWithTransforms(
        string layerId,
        GfxObject layerProps,
        GfxObject allLayersObject,
        SKBitmap currentCanvas,
        Dimensions canvasDimensions,
        EvaluationContext context,
        DeterministicSeedProvider effectiveSeedProvider,
        Dictionary<string, SourceSpan> layerSpans,
        SourceSpan defaultSpan,
        List<GfxDiagnostic> diagnostics,
        Dictionary<string, (SKBitmap Bitmap, Point2D Offset)> renderedLayers,
        out Point2D offset,
        out BlendMode blendMode,
        out float opacity,
        out bool isAdjustmentLayer)
    {
        isAdjustmentLayer = false;
        var layerSpan = layerSpans.GetValueOrDefault(layerId, defaultSpan);
        var layerSize = ExtractLayerDimensions(layerProps, canvasDimensions, layerId, layerSpan, diagnostics);
        offset = ExtractLayerOffset(layerProps, layerId, layerSpan, diagnostics);
        blendMode = ExtractLayerBlendMode(layerProps, layerId, layerSpan, diagnostics);
        opacity = ExtractLayerOpacity(layerProps, layerId, layerSpan, diagnostics);

        if (renderedLayers.TryGetValue(layerId, out var alreadyRendered))
        {
            offset = alreadyRendered.Offset;
            return alreadyRendered.Bitmap;
        }

        var layerType = ExtractLayerType(layerProps, layerId, layerSpan, diagnostics);
        if (layerType is null)
        {
            return null;
        }

        if (!_registry.TryGetRenderer(layerType, out var renderer))
        {
            var available = _registry.RegisteredLayerTypes.Count > 0
                ? string.Join(", ", _registry.RegisteredLayerTypes)
                : "none";
            diagnostics.Add(new GfxDiagnostic(
                "RND004",
                $"Unknown layer type '{layerType}' on layer '{layerId}'. Available layer types: {available}.",
                layerSpan,
                DiagnosticSeverity.Error));
            return null;
        }

        var layerSeedProvider = effectiveSeedProvider;
        var layerRunNumber = context.Run.Number;
        if (layerProps.TryGetProperty("seed", out var layerSeedValue))
        {
            if (GfxSeedValue.TryParse(layerSeedValue, out var explicitLayerSeed))
            {
                layerSeedProvider = new DeterministicSeedProvider(explicitLayerSeed);
                layerRunNumber = 0;
            }
            else
            {
                diagnostics.Add(new GfxDiagnostic(
                    "RND051",
                    $"Seed property on layer '{layerId}' must be a finite, non-negative integer in the unsigned 64-bit range.",
                    layerSpan,
                    DiagnosticSeverity.Error));
            }
        }

        var layerRandom = layerSeedProvider.CreateLayerRandom(layerRunNumber, layerId);
        var layerFrameRandom = layerSeedProvider.CreateRandom(layerRunNumber, context.Frame.Number, $"{layerId}:frame");
        var previousRandom = context.Random;
        var previousFrameRandom = context.FrameRandom;
        var previousLayerId = context.CurrentLayerId;
        context.Random = layerRandom;
        context.FrameRandom = layerFrameRandom;
        context.CurrentLayerId = layerId;

        SKBitmap? ResolveOtherLayerBitmap(string refLayerId)
        {
            if (string.IsNullOrWhiteSpace(refLayerId) || string.Equals(refLayerId, layerId, StringComparison.Ordinal))
            {
                return null;
            }

            if (renderedLayers.TryGetValue(refLayerId, out var existingEntry))
            {
                return existingEntry.Bitmap;
            }

            if (allLayersObject.Properties.TryGetValue(refLayerId, out var refLayerVal)
                && refLayerVal is GfxObject refLayerProps)
            {
                return RenderSingleLayerWithTransforms(
                    refLayerId,
                    refLayerProps,
                    allLayersObject,
                    currentCanvas,
                    canvasDimensions,
                    context,
                    effectiveSeedProvider,
                    layerSpans,
                    defaultSpan,
                    diagnostics,
                    renderedLayers,
                    out Point2D _,
                    out BlendMode _,
                    out float _,
                    out bool _);
            }

            return context.ResolveLayerBitmap?.Invoke(refLayerId, layerSpan);
        }

        SKBitmap? rawBitmap;
        try
        {
            if (renderer is EffectAdjustmentLayerRenderer adjustmentRenderer)
            {
                if (adjustmentRenderer.Effect is PixelShaderEffect)
                {
                    isAdjustmentLayer = false;
                    rawBitmap = new SKBitmap(new SKImageInfo(layerSize.Width, layerSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                    rawBitmap.Erase(SKColors.Transparent);
                    using var canvasSnapshot = ExtractCanvasRegion(currentCanvas, offset, layerSize);
                    var effectContext = new EffectContext(
                        layerId,
                        layerProps,
                        layerSpan,
                        diagnostics,
                        layerRandom,
                        ResolveOtherLayerBitmap,
                        context,
                        canvasSnapshot,
                        canvasSnapshot);
                    adjustmentRenderer.Effect.Apply(rawBitmap, effectContext);
                }
                else
                {
                    isAdjustmentLayer = true;
                    rawBitmap = ExtractCanvasRegion(currentCanvas, offset, layerSize);
                    var effectContext = new EffectContext(
                        layerId,
                        layerProps,
                        layerSpan,
                        diagnostics,
                        layerRandom,
                        ResolveOtherLayerBitmap,
                        context,
                        rawBitmap,
                        rawBitmap);
                    adjustmentRenderer.Effect.Apply(rawBitmap, effectContext);
                }
            }
            else
            {
                var renderContext = new LayerRenderContext(
                    layerId,
                    layerProps,
                    canvasDimensions,
                    layerSize,
                    layerRandom,
                    context,
                    layerSpan,
                    diagnostics);

                rawBitmap = renderer.RenderLayer(renderContext);
            }
        }
        finally
        {
            context.Random = previousRandom;
            context.FrameRandom = previousFrameRandom;
            context.CurrentLayerId = previousLayerId;
        }

        if (rawBitmap is null)
        {
            return null;
        }

        if (!isAdjustmentLayer)
        {
            ApplyInlineLayerEffects(rawBitmap, layerProps, layerId, layerSpan, diagnostics, layerRandom, ResolveOtherLayerBitmap, context, currentCanvas, offset);
        }

        var transformed = ApplyLayerTransformIfNeeded(rawBitmap, layerProps, layerType, layerId, layerSpan, diagnostics);
        if (!ReferenceEquals(transformed, rawBitmap))
        {
            rawBitmap.Dispose();
        }

        ApplyLayerMaskIfNeeded(
            transformed,
            offset,
            layerProps,
            allLayersObject,
            currentCanvas,
            canvasDimensions,
            context,
            effectiveSeedProvider,
            layerSpans,
            defaultSpan,
            diagnostics,
            renderedLayers,
            layerId,
            layerSpan);

        renderedLayers[layerId] = (transformed, offset);
        return transformed;
    }

    private static readonly (string PropName, string EffectName, string? ScalarPropName)[] InlineEffectBindings =
    [
        ("outline", "outline", "width"),
        ("drop_shadow", "drop_shadow", null),
        ("shadow", "drop_shadow", null),
        ("blur", "gaussian_blur", "radius"),
        ("gaussian_blur", "gaussian_blur", "radius"),
        ("box_blur", "gaussian_blur", "radius"),
        ("threshold", "threshold", "level"),
        ("posterize", "posterize", "levels"),
        ("pixelate", "pixelate", "block_size"),
        ("color_adjust", "color_adjust", null),
        ("warp", "warp", "strength"),
        ("displace", "displace", "strength"),
        ("twirl", "twirl", "angle"),
        ("swirl", "twirl", "angle"),
        ("ripple", "ripple", "amplitude"),
        ("spherize", "spherize", "amount"),
        ("pinch", "pinch", "amount"),
        ("kaleidoscope", "kaleidoscope", "segments"),
        ("emboss", "emboss", "strength"),
        ("bevel", "bevel", "depth"),
        ("normal_map", "normal_map", "strength"),
        ("halftone", "halftone", "dot_size"),
        ("dither", "dither", "levels"),
        ("stipple", "stipple", "levels"),
        ("edge_detect", "edge_detect", "threshold"),
        ("sobel", "sobel", "threshold"),
        ("vignette", "vignette", "strength"),
        ("bloom", "bloom", "intensity"),
        ("glow", "glow", "intensity"),
        ("chromatic_aberration", "chromatic_aberration", "offset"),
        ("crt", "crt", "scanline_opacity"),
        ("scanlines", "scanlines", "scanline_opacity"),
        ("oil_paint", "oil_paint", "radius"),
        ("kuwahara", "kuwahara", "radius"),
        ("shader", "shader", "fn"),
        ("pixel_shader", "pixel_shader", "fn")
    ];

    private void ApplyInlineLayerEffects(
        SKBitmap bitmap,
        GfxObject layerProps,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics,
        IGfxRandom layerRandom,
        Func<string, SKBitmap?> resolveLayerBitmap,
        EvaluationContext context,
        SKBitmap currentCanvas,
        Point2D layerOffset)
    {
        SKBitmap? canvasRegion = null;
        try
        {
            foreach (var (propName, effectName, scalarPropName) in InlineEffectBindings)
            {
                if (!layerProps.TryGetProperty(propName, out var propVal))
                {
                    continue;
                }

                var effectProps = scalarPropName is not null
                    ? CoerceEffectProps(propVal, scalarPropName)
                    : propVal as GfxObject;

                if (effectProps is not null && _effectRegistry.TryGetEffect(effectName, out var inlineEffect))
                {
                    canvasRegion ??= ExtractCanvasRegion(currentCanvas, layerOffset, new Dimensions(bitmap.Width, bitmap.Height));
                    inlineEffect.Apply(bitmap, new EffectContext(
                        layerId,
                        effectProps,
                        layerSpan,
                        diagnostics,
                        ResolveEffectRandom(effectProps, layerId, layerSpan, diagnostics, layerRandom),
                        resolveLayerBitmap,
                        context,
                        canvasBitmap: canvasRegion));
                }
            }

            // effects = [ { type = ..., ... }, ... ]
            if (layerProps.TryGetProperty("effects", out var effectsVal) && effectsVal is GfxArray effectsArr)
            {
                foreach (var item in effectsArr.Items)
                {
                    if (item is not GfxObject fxObj || !fxObj.TryGetProperty("type", out var fxTypeVal))
                    {
                        diagnostics.Add(new GfxDiagnostic("FX008", $"Each item in 'effects' on layer '{layerId}' must be an object with a 'type' property.", layerSpan, DiagnosticSeverity.Error));
                        continue;
                    }

                    var fxType = fxTypeVal switch
                    {
                        GfxSymbol s => s.Identifier,
                        GfxString str => str.Value,
                        _ => string.Empty
                    };

                    if (!_effectRegistry.TryGetEffect(fxType, out var effect))
                    {
                        diagnostics.Add(new GfxDiagnostic("FX008", $"Unknown effect type '{fxType}' on layer '{layerId}'.", layerSpan, DiagnosticSeverity.Error));
                        continue;
                    }

                    canvasRegion ??= ExtractCanvasRegion(currentCanvas, layerOffset, new Dimensions(bitmap.Width, bitmap.Height));
                    effect.Apply(bitmap, new EffectContext(
                        layerId,
                        fxObj,
                        layerSpan,
                        diagnostics,
                        ResolveEffectRandom(fxObj, layerId, layerSpan, diagnostics, layerRandom),
                        resolveLayerBitmap,
                        context,
                        canvasBitmap: canvasRegion));
                }
            }
        }
        finally
        {
            canvasRegion?.Dispose();
        }
    }

    private static IGfxRandom ResolveEffectRandom(
        GfxObject effectProperties,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics,
        IGfxRandom layerRandom)
    {
        if (!effectProperties.TryGetProperty("seed", out var seedValue))
        {
            return layerRandom;
        }

        if (GfxSeedValue.TryParse(seedValue, out var effectSeed))
        {
            return new GfxRandom(effectSeed);
        }

        diagnostics.Add(new GfxDiagnostic(
            "RND051",
            $"Seed property on an effect on layer '{layerId}' must be a finite, non-negative integer in the unsigned 64-bit range.",
            layerSpan,
            DiagnosticSeverity.Error));
        return layerRandom;
    }

    private static GfxObject? CoerceEffectProps(GfxValue val, string scalarPropName) =>
        val switch
        {
            GfxObject obj => obj,
            GfxNumber or GfxFunctionRef or GfxString or GfxSymbol =>
                new GfxObject(new Dictionary<string, GfxValue>(StringComparer.Ordinal) { [scalarPropName] = val }, [scalarPropName]),
            _ => null
        };

    private static SKBitmap ExtractCanvasRegion(SKBitmap canvas, Point2D offset, Dimensions regionSize)
    {
        var region = new SKBitmap(new SKImageInfo(regionSize.Width, regionSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        region.Erase(SKColors.Transparent);

        var ox = (int)Math.Round(offset.X);
        var oy = (int)Math.Round(offset.Y);

        for (var y = 0; y < regionSize.Height; y++)
        {
            var cy = oy + y;
            if (cy < 0 || cy >= canvas.Height)
            {
                continue;
            }

            for (var x = 0; x < regionSize.Width; x++)
            {
                var cx = ox + x;
                if (cx < 0 || cx >= canvas.Width)
                {
                    continue;
                }

                region.SetPixel(x, y, canvas.GetPixel(cx, cy));
            }
        }

        return region;
    }

    private static void WriteRegionInPlace(SKBitmap canvas, SKBitmap region, Point2D offset)
    {
        var ox = (int)Math.Round(offset.X);
        var oy = (int)Math.Round(offset.Y);

        for (var y = 0; y < region.Height; y++)
        {
            var cy = oy + y;
            if (cy < 0 || cy >= canvas.Height)
            {
                continue;
            }

            for (var x = 0; x < region.Width; x++)
            {
                var cx = ox + x;
                if (cx < 0 || cx >= canvas.Width)
                {
                    continue;
                }

                canvas.SetPixel(cx, cy, region.GetPixel(x, y));
            }
        }
    }

    private static GfxObject? TryGetTransformObject(
        GfxObject layerProps,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic>? diagnostics)
    {
        if (!layerProps.TryGetProperty("transform", out var transformVal))
        {
            return null;
        }

        if (transformVal is GfxObject transformObj)
        {
            return transformObj;
        }

        diagnostics?.Add(new GfxDiagnostic(
            "RND036",
            $"Property 'transform' on layer '{layerId}' must be an object block.",
            layerSpan,
            DiagnosticSeverity.Error));
        return null;
    }

    private static SKBitmap ApplyLayerTransformIfNeeded(
        SKBitmap source,
        GfxObject layerProps,
        string layerType,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        var transformObj = TryGetTransformObject(layerProps, layerId, layerSpan, diagnostics);
        var shapeHandlesTopLevelRotation = layerType.Equals("rect", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("polygon", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("star", StringComparison.OrdinalIgnoreCase);

        var rotation = 0.0f;
        if (transformObj is not null
            && (transformObj.TryGetProperty("rotation", out var tRotVal) || transformObj.TryGetProperty("rotate", out tRotVal)))
        {
            if (tRotVal is GfxNumber rotNum)
            {
                rotation = rotNum.AsSingle();
            }
            else
            {
                diagnostics.Add(new GfxDiagnostic("RND036", $"Property 'rotation' in 'transform' on layer '{layerId}' must be a number.", layerSpan, DiagnosticSeverity.Error));
            }
        }
        else if (!shapeHandlesTopLevelRotation
            && (layerProps.TryGetProperty("rotation", out var rotVal) || layerProps.TryGetProperty("rotate", out rotVal)))
        {
            if (rotVal is GfxNumber rotNum)
            {
                rotation = rotNum.AsSingle();
            }
            else
            {
                diagnostics.Add(new GfxDiagnostic("RND036", $"Property 'rotation' on layer '{layerId}' must be a number.", layerSpan, DiagnosticSeverity.Error));
            }
        }
        else if (shapeHandlesTopLevelRotation
            && !layerProps.Properties.ContainsKey("rotation")
            && layerProps.TryGetProperty("rotate", out var rotateAliasVal))
        {
            if (rotateAliasVal is GfxNumber rotNum)
            {
                rotation = rotNum.AsSingle();
            }
            else
            {
                diagnostics.Add(new GfxDiagnostic("RND036", $"Property 'rotate' on layer '{layerId}' must be a number.", layerSpan, DiagnosticSeverity.Error));
            }
        }

        var (scaleX, scaleY) = ExtractLayerTransformScale(layerProps, transformObj, layerType, layerId, layerSpan, diagnostics);
        var (shearX, shearY) = ExtractLayerShear(layerProps, transformObj, layerId, layerSpan, diagnostics);
        var flipX = ExtractLayerBoolean(layerProps, transformObj, "flip_x", layerId, layerSpan, diagnostics);
        var flipY = ExtractLayerBoolean(layerProps, transformObj, "flip_y", layerId, layerSpan, diagnostics);
        var wrapMode = ExtractLayerWrapMode(layerProps, transformObj, layerType, layerId, layerSpan, diagnostics);

        if (wrapMode != LayerWrapMode.None)
        {
            return ApplyWrappedLayerTransform(
                source,
                layerProps,
                transformObj,
                layerId,
                layerSpan,
                diagnostics,
                rotation,
                scaleX,
                scaleY,
                shearX,
                shearY,
                flipX,
                flipY,
                wrapMode);
        }

        if (Math.Abs(rotation) < 1e-5f
            && Math.Abs(scaleX - 1.0f) < 1e-5f
            && Math.Abs(scaleY - 1.0f) < 1e-5f
            && Math.Abs(shearX) < 1e-5f
            && Math.Abs(shearY) < 1e-5f
            && !flipX
            && !flipY)
        {
            return source;
        }

        var width = source.Width;
        var height = source.Height;

        // Exact pixel flip when there is no rotation or scaling
        if (Math.Abs(rotation) < 1e-5f
            && Math.Abs(scaleX - 1.0f) < 1e-5f
            && Math.Abs(scaleY - 1.0f) < 1e-5f
            && Math.Abs(shearX) < 1e-5f
            && Math.Abs(shearY) < 1e-5f)
        {
            var flipped = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            for (var y = 0; y < height; y++)
            {
                var sy = flipY ? (height - 1 - y) : y;
                for (var x = 0; x < width; x++)
                {
                    var sx = flipX ? (width - 1 - x) : x;
                    flipped.SetPixel(x, y, source.GetPixel(sx, sy));
                }
            }

            return flipped;
        }

        var (anchorX, anchorY) = ExtractLayerAnchor(layerProps, transformObj, width, height);
        var dest = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        dest.Erase(SKColors.Transparent);

        var finalScaleX = flipX ? -scaleX : scaleX;
        var finalScaleY = flipY ? -scaleY : scaleY;

        using var canvas = new SKCanvas(dest);
        canvas.Translate(anchorX, anchorY);
        canvas.RotateDegrees(rotation);
        canvas.Skew(shearX, shearY);
        canvas.Scale(finalScaleX, finalScaleY);
        canvas.Translate(-anchorX, -anchorY);

        var useNearest = ExtractLayerUseNearest(layerProps, transformObj);

        using var paint = new SKPaint
        {
            FilterQuality = useNearest ? SKFilterQuality.None : SKFilterQuality.High,
            IsAntialias = !useNearest
        };
        canvas.DrawBitmap(source, 0, 0, paint);
        canvas.Flush();
        return dest;
    }

    private static bool ExtractLayerUseNearest(GfxObject layerProps, GfxObject? transformObj)
    {
        if (transformObj is not null && transformObj.TryGetProperty("resample", out var tResVal))
        {
            return (tResVal is GfxSymbol ts && string.Equals(ts.Identifier, "nearest", StringComparison.OrdinalIgnoreCase))
                || (tResVal is GfxString tstr && string.Equals(tstr.Value, "nearest", StringComparison.OrdinalIgnoreCase));
        }

        return layerProps.TryGetProperty("resample", out var resVal)
            && ((resVal is GfxSymbol s && string.Equals(s.Identifier, "nearest", StringComparison.OrdinalIgnoreCase))
                || (resVal is GfxString str && string.Equals(str.Value, "nearest", StringComparison.OrdinalIgnoreCase)));
    }

    private static SKBitmap ApplyWrappedLayerTransform(
        SKBitmap source,
        GfxObject layerProps,
        GfxObject? transformObj,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics,
        float rotation,
        float scaleX,
        float scaleY,
        float shearX,
        float shearY,
        bool flipX,
        bool flipY,
        LayerWrapMode wrapMode)
    {
        var width = source.Width;
        var height = source.Height;
        var (anchorX, anchorY) = ExtractLayerAnchor(layerProps, transformObj, width, height);
        var finalScaleX = flipX ? -scaleX : scaleX;
        var finalScaleY = flipY ? -scaleY : scaleY;
        var radians = rotation * (Math.PI / 180.0);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        // Match the transform order used by SKCanvas above: scale, shear, then rotate around the anchor.
        var a = finalScaleX * (cos - (sin * shearY));
        var b = finalScaleY * ((cos * shearX) - sin);
        var c = finalScaleX * (sin + (cos * shearY));
        var d = finalScaleY * ((sin * shearX) + cos);
        var determinant = (a * d) - (b * c);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
        {
            diagnostics.Add(new GfxDiagnostic(
                "RND036",
                $"Layer '{layerId}' cannot use wrap_mode with a singular transform.",
                layerSpan,
                DiagnosticSeverity.Error));
            var empty = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            empty.Erase(SKColors.Transparent);
            return empty;
        }

        var useNearest = ExtractLayerUseNearest(layerProps, transformObj);
        var dest = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var dx = (x + 0.5) - anchorX;
                var dy = (y + 0.5) - anchorY;
                var sourceX = (((d * dx) - (b * dy)) / determinant) + anchorX;
                var sourceY = (((-c * dx) + (a * dy)) / determinant) + anchorY;
                dest.SetPixel(x, y, SampleWrappedPixel(source, sourceX, sourceY, wrapMode, useNearest));
            }
        }

        return dest;
    }

    private static LayerWrapMode ExtractLayerWrapMode(
        GfxObject layerProps,
        GfxObject? transformObj,
        string layerType,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        var propertyName = "wrap_mode";
        GfxValue? value = null;
        var fromTransformBlock = false;

        if (transformObj is not null)
        {
            if (transformObj.TryGetProperty("wrap_mode", out value))
            {
                propertyName = "wrap_mode";
                fromTransformBlock = true;
            }
            else if (transformObj.TryGetProperty("wrap", out value))
            {
                propertyName = "wrap";
                fromTransformBlock = true;
            }
        }

        if (!fromTransformBlock)
        {
            if (!layerProps.TryGetProperty("wrap_mode", out value))
            {
                if (!layerProps.TryGetProperty("wrap", out value))
                {
                    return LayerWrapMode.None;
                }

                // Preserve the boolean `wrap` option on standalone spatial-effect layers.
                if (value is GfxBoolean)
                {
                    if (layerType.Equals("warp", StringComparison.OrdinalIgnoreCase)
                        || layerType.Equals("displace", StringComparison.OrdinalIgnoreCase))
                    {
                        return LayerWrapMode.None;
                    }
                }

                propertyName = "wrap";
            }
        }

        var modeName = value switch
        {
            GfxSymbol symbol => symbol.Identifier,
            GfxString text => text.Value,
            _ => string.Empty
        };
        var mode = modeName.ToLowerInvariant() switch
        {
            "none" => LayerWrapMode.None,
            "repeat" => LayerWrapMode.Repeat,
            "mirror" => LayerWrapMode.Mirror,
            "clamp" => LayerWrapMode.Clamp,
            _ => (LayerWrapMode)(-1)
        };

        if (Enum.IsDefined(mode))
        {
            return mode;
        }

        diagnostics.Add(new GfxDiagnostic(
            "RND036",
            $"Property '{propertyName}' on layer '{layerId}' must be one of: none, repeat, mirror, or clamp.",
            layerSpan,
            DiagnosticSeverity.Error));
        return LayerWrapMode.None;
    }

    private static SKColor SampleWrappedPixel(
        SKBitmap source,
        double sourceX,
        double sourceY,
        LayerWrapMode wrapMode,
        bool nearest)
    {
        if (nearest)
        {
            var x = WrapPixelIndex((int)Math.Floor(sourceX), source.Width, wrapMode);
            var y = WrapPixelIndex((int)Math.Floor(sourceY), source.Height, wrapMode);
            return source.GetPixel(x, y);
        }

        // Bitmap coordinates refer to pixel edges; shift to pixel-center coordinates for bilinear sampling.
        var pixelX = sourceX - 0.5;
        var pixelY = sourceY - 0.5;
        var x0 = (int)Math.Floor(pixelX);
        var y0 = (int)Math.Floor(pixelY);
        var tx = pixelX - x0;
        var ty = pixelY - y0;
        var c00 = source.GetPixel(WrapPixelIndex(x0, source.Width, wrapMode), WrapPixelIndex(y0, source.Height, wrapMode));
        var c10 = source.GetPixel(WrapPixelIndex(x0 + 1, source.Width, wrapMode), WrapPixelIndex(y0, source.Height, wrapMode));
        var c01 = source.GetPixel(WrapPixelIndex(x0, source.Width, wrapMode), WrapPixelIndex(y0 + 1, source.Height, wrapMode));
        var c11 = source.GetPixel(WrapPixelIndex(x0 + 1, source.Width, wrapMode), WrapPixelIndex(y0 + 1, source.Height, wrapMode));
        return InterpolatePremultiplied(c00, c10, c01, c11, tx, ty);
    }

    private static int WrapPixelIndex(int index, int length, LayerWrapMode wrapMode)
    {
        if (wrapMode == LayerWrapMode.Clamp)
        {
            return Math.Clamp(index, 0, length - 1);
        }

        if (wrapMode == LayerWrapMode.Mirror)
        {
            var period = 2 * length;
            var wrapped = ((index % period) + period) % period;
            return wrapped < length ? wrapped : period - 1 - wrapped;
        }

        return ((index % length) + length) % length;
    }

    private static SKColor InterpolatePremultiplied(SKColor c00, SKColor c10, SKColor c01, SKColor c11, double tx, double ty)
    {
        static double Bilinear(double v00, double v10, double v01, double v11, double x, double y) =>
            ((v00 * (1.0 - x)) + (v10 * x)) * (1.0 - y)
            + ((v01 * (1.0 - x)) + (v11 * x)) * y;

        var a00 = c00.Alpha / 255.0;
        var a10 = c10.Alpha / 255.0;
        var a01 = c01.Alpha / 255.0;
        var a11 = c11.Alpha / 255.0;
        var alpha = Bilinear(c00.Alpha, c10.Alpha, c01.Alpha, c11.Alpha, tx, ty);
        if (alpha <= 0.0)
        {
            return SKColors.Transparent;
        }

        var alphaFraction = alpha / 255.0;
        var red = Bilinear(c00.Red * a00, c10.Red * a10, c01.Red * a01, c11.Red * a11, tx, ty) / alphaFraction;
        var green = Bilinear(c00.Green * a00, c10.Green * a10, c01.Green * a01, c11.Green * a11, tx, ty) / alphaFraction;
        var blue = Bilinear(c00.Blue * a00, c10.Blue * a10, c01.Blue * a01, c11.Blue * a11, tx, ty) / alphaFraction;
        return new SKColor(ToByte(red), ToByte(green), ToByte(blue), ToByte(alpha));

        static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private enum LayerWrapMode
    {
        None,
        Repeat,
        Mirror,
        Clamp
    }

    private static (float X, float Y) ExtractLayerShear(
        GfxObject layerProps,
        GfxObject? transformObj,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if ((transformObj is null || !transformObj.TryGetProperty("shear", out var shearVal))
            && !layerProps.TryGetProperty("shear", out shearVal))
        {
            return (0.0f, 0.0f);
        }

        if (shearVal.TryCoerceToPoint2D(out var shear)
            && float.IsFinite(shear.X)
            && float.IsFinite(shear.Y))
        {
            return (shear.X, shear.Y);
        }

        diagnostics.Add(new GfxDiagnostic(
            "RND036",
            $"Property 'shear' on layer '{layerId}' must be an object with finite numeric x and y values.",
            layerSpan,
            DiagnosticSeverity.Error));
        return (0.0f, 0.0f);
    }

    private static (float ScaleX, float ScaleY) ExtractLayerTransformScale(
        GfxObject layerProps,
        GfxObject? transformObj,
        string layerType,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        // Inside `transform = { ... }`, `scale` and `transform_scale` are ALWAYS unambiguous 2D transform scales
        if (transformObj is not null
            && (transformObj.TryGetProperty("transform_scale", out var tScaleVal) || transformObj.TryGetProperty("scale", out tScaleVal)))
        {
            if (tScaleVal is GfxNumber tNum)
            {
                var s = tNum.AsSingle();
                return (s, s);
            }

            if (tScaleVal.TryCoerceToPoint2D(out var tPt))
            {
                return (tPt.X, tPt.Y);
            }

            diagnostics.Add(new GfxDiagnostic("RND036", $"Invalid 'scale' property in 'transform' on layer '{layerId}'.", layerSpan, DiagnosticSeverity.Error));
            return (1.0f, 1.0f);
        }

        var isNoiseLayer = layerType.Equals("fractal_clouds", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("perlin", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("simplex", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("worley", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("voronoi", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("wood", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("marble", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("caustics", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("flow_field", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("curl_noise", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("warp", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("displace", StringComparison.OrdinalIgnoreCase);

        if (!layerProps.TryGetProperty("transform_scale", out var scaleVal))
        {
            if (!layerProps.TryGetProperty("scale", out scaleVal))
            {
                return (1.0f, 1.0f);
            }

            // On noise layers, scalar 'scale' is the noise frequency, not a 2D bitmap transform scale
            if (isNoiseLayer && scaleVal is GfxNumber)
            {
                return (1.0f, 1.0f);
            }
        }

        if (scaleVal is GfxNumber num)
        {
            var s = num.AsSingle();
            return (s, s);
        }

        if (scaleVal.TryCoerceToPoint2D(out var pt))
        {
            return (pt.X, pt.Y);
        }

        diagnostics.Add(new GfxDiagnostic("RND036", $"Invalid 'scale' property on layer '{layerId}'.", layerSpan, DiagnosticSeverity.Error));
        return (1.0f, 1.0f);
    }

    private static bool ExtractLayerBoolean(
        GfxObject layerProps,
        GfxObject? transformObj,
        string propName,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if ((transformObj is null || !transformObj.TryGetProperty(propName, out var val))
            && !layerProps.TryGetProperty(propName, out val))
        {
            return false;
        }

        if (val is GfxBoolean b)
        {
            return b.Value;
        }

        diagnostics.Add(new GfxDiagnostic("RND036", $"Property '{propName}' on layer '{layerId}' must be a boolean.", layerSpan, DiagnosticSeverity.Error));
        return false;
    }

    private static (float X, float Y) ExtractLayerAnchor(GfxObject layerProps, GfxObject? transformObj, int width, int height)
    {
        GfxValue? anchorVal = null;
        var found = transformObj is not null
            && (transformObj.TryGetProperty("anchor", out anchorVal) || transformObj.TryGetProperty("pivot", out anchorVal));

        if (!found
            && !layerProps.TryGetProperty("anchor", out anchorVal)
            && !layerProps.TryGetProperty("pivot", out anchorVal))
        {
            return (width * 0.5f, height * 0.5f);
        }

        if (anchorVal is not null && anchorVal.TryCoerceToPoint2D(out var pt))
        {
            if (pt.X is >= 0.0f and <= 1.0f && pt.Y is >= 0.0f and <= 1.0f)
            {
                return (pt.X * width, pt.Y * height);
            }

            return (pt.X, pt.Y);
        }

        return (width * 0.5f, height * 0.5f);
    }

    private void ApplyLayerMaskIfNeeded(
        SKBitmap targetBitmap,
        Point2D targetOffset,
        GfxObject layerProps,
        GfxObject allLayersObject,
        SKBitmap currentCanvas,
        Dimensions canvasDimensions,
        EvaluationContext context,
        DeterministicSeedProvider effectiveSeedProvider,
        Dictionary<string, SourceSpan> layerSpans,
        SourceSpan defaultSpan,
        List<GfxDiagnostic> diagnostics,
        Dictionary<string, (SKBitmap Bitmap, Point2D Offset)> renderedLayers,
        string layerId,
        SourceSpan layerSpan)
    {
        if (!layerProps.TryGetProperty("mask", out var maskVal))
        {
            return;
        }

        var maskLayerId = maskVal switch
        {
            GfxString str => str.Value,
            GfxSymbol sym => sym.Identifier,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(maskLayerId) || maskLayerId == layerId)
        {
            diagnostics.Add(new GfxDiagnostic("RND037", $"Invalid 'mask' property on layer '{layerId}'.", layerSpan, DiagnosticSeverity.Error));
            return;
        }

        SKBitmap? maskBitmap;
        Point2D maskOffset;
        if (renderedLayers.TryGetValue(maskLayerId, out var maskEntry))
        {
            maskBitmap = maskEntry.Bitmap;
            maskOffset = maskEntry.Offset;
        }
        else
        {
            if (!allLayersObject.Properties.TryGetValue(maskLayerId, out var maskLayerObjVal)
                || maskLayerObjVal is not GfxObject maskLayerProps)
            {
                diagnostics.Add(new GfxDiagnostic("RND037", $"Mask layer '{maskLayerId}' referenced by layer '{layerId}' does not exist.", layerSpan, DiagnosticSeverity.Error));
                return;
            }

            maskBitmap = RenderSingleLayerWithTransforms(
                maskLayerId,
                maskLayerProps,
                allLayersObject,
                currentCanvas,
                canvasDimensions,
                context,
                effectiveSeedProvider,
                layerSpans,
                defaultSpan,
                diagnostics,
                renderedLayers,
                out maskOffset,
                out _,
                out _,
                out _);
        }

        if (maskBitmap is null)
        {
            return;
        }

        // Determine if mask has non-zero RGB luminance; if all opaque pixels are black, use alpha channel only
        var hasLuminance = false;
        for (var my = 0; my < maskBitmap.Height && !hasLuminance; my++)
        {
            for (var mx = 0; mx < maskBitmap.Width; mx++)
            {
                var mp = maskBitmap.GetPixel(mx, my);
                if (mp.Alpha > 0 && (mp.Red > 0 || mp.Green > 0 || mp.Blue > 0))
                {
                    hasLuminance = true;
                    break;
                }
            }
        }

        var deltaX = (int)Math.Round(targetOffset.X - maskOffset.X);
        var deltaY = (int)Math.Round(targetOffset.Y - maskOffset.Y);

        for (var y = 0; y < targetBitmap.Height; y++)
        {
            var my = y + deltaY;
            for (var x = 0; x < targetBitmap.Width; x++)
            {
                var mx = x + deltaX;
                if (mx < 0 || mx >= maskBitmap.Width || my < 0 || my >= maskBitmap.Height)
                {
                    targetBitmap.SetPixel(x, y, SKColors.Transparent);
                    continue;
                }

                var srcPixel = targetBitmap.GetPixel(x, y);
                if (srcPixel.Alpha == 0)
                {
                    continue;
                }

                var maskPixel = maskBitmap.GetPixel(mx, my);
                var alphaFactor = maskPixel.Alpha / 255.0f;
                var lumFactor = hasLuminance
                    ? ((0.2126f * maskPixel.Red) + (0.7152f * maskPixel.Green) + (0.0722f * maskPixel.Blue)) / 255.0f
                    : 1.0f;
                var maskFactor = Math.Clamp(alphaFactor * lumFactor, 0.0f, 1.0f);

                if (maskFactor <= 0.0f)
                {
                    targetBitmap.SetPixel(x, y, SKColors.Transparent);
                }
                else if (maskFactor < 1.0f)
                {
                    var newA = (byte)Math.Round(srcPixel.Alpha * maskFactor);
                    targetBitmap.SetPixel(x, y, new SKColor(srcPixel.Red, srcPixel.Green, srcPixel.Blue, newA));
                }
            }
        }
    }

    private static Dimensions? ResolveCanvasDimensions(
        GfxEvaluator evaluator,
        List<GfxDiagnostic> diagnostics,
        SourceSpan fallbackSpan)
    {
        var dimsResult = evaluator.EvaluatePath("output.dimensions");
        if (!dimsResult.HasErrors && dimsResult.Value is not null && dimsResult.Value.TryCoerceToDimensions(out var dims))
        {
            return dims;
        }

        diagnostics.AddRange(dimsResult.Diagnostics);
        if (diagnostics.Count == 0)
        {
            diagnostics.Add(new GfxDiagnostic(
                "RND005",
                "Missing or invalid 'output.dimensions' (expected '{ width = <positive int>, height = <positive int> }').",
                fallbackSpan,
                DiagnosticSeverity.Error));
        }

        return null;
    }

    private static bool IsLayerVisible(
        GfxObject layerProps,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if (layerProps.TryGetProperty("visible", out var visibleVal))
        {
            if (visibleVal is GfxBoolean b)
            {
                return b.Value;
            }

            diagnostics.Add(new GfxDiagnostic("RND006", $"Property 'visible' on layer '{layerId}' must be a boolean.", layerSpan, DiagnosticSeverity.Error));
        }

        if (layerProps.TryGetProperty("enabled", out var enabledVal))
        {
            if (enabledVal is GfxBoolean b)
            {
                return b.Value;
            }

            diagnostics.Add(new GfxDiagnostic("RND006", $"Property 'enabled' on layer '{layerId}' must be a boolean.", layerSpan, DiagnosticSeverity.Error));
        }

        return true;
    }

    private static string? ExtractLayerType(
        GfxObject layerProps,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if (!layerProps.TryGetProperty("type", out var typeVal))
        {
            diagnostics.Add(new GfxDiagnostic("RND003", $"Layer '{layerId}' is missing required property 'type'.", layerSpan, DiagnosticSeverity.Error));
            return null;
        }

        return typeVal switch
        {
            GfxSymbol symbol => symbol.Identifier,
            GfxString str => str.Value,
            _ => ReportInvalidLayerType(layerId, layerSpan, diagnostics)
        };
    }

    private static string? ReportInvalidLayerType(string layerId, SourceSpan layerSpan, List<GfxDiagnostic> diagnostics)
    {
        diagnostics.Add(new GfxDiagnostic("RND003", $"Property 'type' on layer '{layerId}' must be an identifier or string.", layerSpan, DiagnosticSeverity.Error));
        return null;
    }

    private static Dimensions ExtractLayerDimensions(
        GfxObject layerProps,
        Dimensions canvasDimensions,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if (!layerProps.TryGetProperty("size", out var sizeVal))
        {
            return canvasDimensions;
        }

        if (sizeVal.TryCoerceToDimensions(out var dims))
        {
            return dims;
        }

        diagnostics.Add(new GfxDiagnostic("RND007", $"Invalid 'size' property on layer '{layerId}'.", layerSpan, DiagnosticSeverity.Error));
        return canvasDimensions;
    }

    private static Point2D ExtractLayerOffset(
        GfxObject layerProps,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if (layerProps.TryGetProperty("transform", out var tVal) && tVal is GfxObject transformObj)
        {
            if (transformObj.TryGetProperty("offset", out var tOffsetVal))
            {
                if (tOffsetVal.TryCoerceToPoint2D(out var pt))
                {
                    return pt;
                }

                diagnostics.Add(new GfxDiagnostic("RND008", $"Invalid 'offset' property in 'transform' on layer '{layerId}' (expected '{{ x = <number>, y = <number> }}').", layerSpan, DiagnosticSeverity.Error));
                return new Point2D(0, 0);
            }

            if (transformObj.TryGetProperty("position", out var tPosVal))
            {
                if (tPosVal.TryCoerceToPoint2D(out var pt))
                {
                    return pt;
                }

                diagnostics.Add(new GfxDiagnostic("RND008", $"Invalid 'position' property in 'transform' on layer '{layerId}' (expected '{{ x = <number>, y = <number> }}').", layerSpan, DiagnosticSeverity.Error));
                return new Point2D(0, 0);
            }
        }

        var layerType = layerProps.TryGetProperty("type", out var tv)
            ? tv switch { GfxSymbol s => s.Identifier, GfxString str => str.Value, _ => string.Empty }
            : string.Empty;
        var usesOffsetAsEffectParam = layerType.Equals("chromatic_aberration", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("drop_shadow", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("shadow", StringComparison.OrdinalIgnoreCase);

        if (!usesOffsetAsEffectParam && layerProps.TryGetProperty("offset", out var offsetVal))
        {
            if (offsetVal.TryCoerceToPoint2D(out var pt))
            {
                return pt;
            }

            diagnostics.Add(new GfxDiagnostic("RND008", $"Invalid 'offset' property on layer '{layerId}' (expected '{{ x = <number>, y = <number> }}').", layerSpan, DiagnosticSeverity.Error));
        }

        if (layerProps.TryGetProperty("position", out var posVal))
        {
            if (posVal.TryCoerceToPoint2D(out var pt))
            {
                return pt;
            }

            diagnostics.Add(new GfxDiagnostic("RND008", $"Invalid 'position' property on layer '{layerId}' (expected '{{ x = <number>, y = <number> }}').", layerSpan, DiagnosticSeverity.Error));
        }

        return new Point2D(0, 0);
    }

    private static BlendMode ExtractLayerBlendMode(
        GfxObject layerProps,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if (!layerProps.TryGetProperty("mix", out var mixVal))
        {
            return BlendMode.Normal;
        }

        if (mixVal.TryCoerceToBlendMode(out var blendMode))
        {
            return blendMode;
        }

        diagnostics.Add(new GfxDiagnostic(
            "RND009",
            $"Unknown blend mode '{mixVal.ToDisplayString()}' in 'mix' property on layer '{layerId}'.",
            layerSpan,
            DiagnosticSeverity.Error));
        return BlendMode.Normal;
    }

    private static float ExtractLayerOpacity(
        GfxObject layerProps,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if (!layerProps.TryGetProperty("opacity", out var opacityVal))
        {
            return 1.0f;
        }

        if (opacityVal is GfxNumber num)
        {
            return Math.Clamp(num.AsSingle(), 0.0f, 1.0f);
        }

        diagnostics.Add(new GfxDiagnostic("RND010", $"Property 'opacity' on layer '{layerId}' must be a number between 0 and 1.", layerSpan, DiagnosticSeverity.Error));
        return 1.0f;
    }

    private void ValidateLayerProperties(
        string layerId,
        GfxObject layerProps,
        SourceSpan layerSpan,
        LayerAstInfo? layerAst,
        List<GfxDiagnostic> diagnostics)
    {
        if (!layerProps.TryGetProperty("type", out var typeVal))
        {
            return;
        }

        var layerType = typeVal switch
        {
            GfxSymbol s => s.Identifier,
            GfxString str => str.Value,
            _ => null
        };

        if (layerType is null || !_registry.TryGetRenderer(layerType, out var renderer))
        {
            return;
        }

        var validLayerProperties = new HashSet<string>(UniversalLayerProperties, StringComparer.OrdinalIgnoreCase);
        foreach (var effectName in _allEffectPropertyNames)
        {
            validLayerProperties.Add(effectName);
        }

        foreach (var rendererProp in renderer.SupportedProperties)
        {
            validLayerProperties.Add(rendererProp);
        }

        foreach (var propName in layerProps.OrderedKeys)
        {
            if (!validLayerProperties.Contains(propName))
            {
                var suggestion = PropertySuggestionHelper.GetSuggestionSuffix(propName, validLayerProperties);
                var propSpan = layerAst?.PropertySpans.GetValueOrDefault(propName) ?? layerSpan;
                diagnostics.Add(new GfxDiagnostic(
                    "RND050",
                    $"Unknown property '{propName}' on layer '{layerId}' of type '{layerType}'.{suggestion}",
                    propSpan,
                    DiagnosticSeverity.Warning));
            }
        }

        // Validate `transform = { ... }` sub-properties
        if (layerProps.TryGetProperty("transform", out var transformVal) && transformVal is GfxObject transformProps)
        {
            var transformSpans = layerAst?.InlineEffectPropertySpans.GetValueOrDefault("transform");
            foreach (var subProp in transformProps.OrderedKeys)
            {
                if (!TransformBlockProperties.Contains(subProp))
                {
                    var suggestion = PropertySuggestionHelper.GetSuggestionSuffix(subProp, TransformBlockProperties);
                    var subSpan = transformSpans?.GetValueOrDefault(subProp)
                        ?? layerAst?.PropertySpans.GetValueOrDefault("transform")
                        ?? layerSpan;
                    diagnostics.Add(new GfxDiagnostic(
                        "RND050",
                        $"Unknown property '{subProp}' in 'transform' block on layer '{layerId}'.{suggestion}",
                        subSpan,
                        DiagnosticSeverity.Warning));
                }
            }
        }

        // Validate inline effects on this layer
        foreach (var (propName, effectName, _) in InlineEffectBindings)
        {
            if (layerProps.TryGetProperty(propName, out var inlineVal) && inlineVal is GfxObject effectProps)
            {
                if (_effectRegistry.TryGetEffect(effectName, out var inlineEffect))
                {
                    ValidateEffectProperties(
                        layerId,
                        propName,
                        inlineEffect,
                        effectProps,
                        layerSpan,
                        layerAst?.InlineEffectPropertySpans.GetValueOrDefault(propName),
                        diagnostics);
                }
            }
        }

        // Validate effects in `effects = [ ... ]`
        if (layerProps.TryGetProperty("effects", out var effectsVal) && effectsVal is GfxArray effectsArr)
        {
            for (var i = 0; i < effectsArr.Items.Count; i++)
            {
                if (effectsArr.Items[i] is GfxObject fxObj && fxObj.TryGetProperty("type", out var fxTypeVal))
                {
                    var fxType = fxTypeVal switch
                    {
                        GfxSymbol s => s.Identifier,
                        GfxString str => str.Value,
                        _ => string.Empty
                    };

                    if (_effectRegistry.TryGetEffect(fxType, out var effect))
                    {
                        Dictionary<string, SourceSpan>? itemSpans = null;
                        if (layerAst is not null && i < layerAst.ArrayEffectPropertySpans.Count)
                        {
                            itemSpans = layerAst.ArrayEffectPropertySpans[i];
                        }

                        ValidateEffectProperties(
                            layerId,
                            fxType,
                            effect,
                            fxObj,
                            layerSpan,
                            itemSpans,
                            diagnostics);
                    }
                }
            }
        }
    }

    private static void ValidateEffectProperties(
        string layerId,
        string effectType,
        ILayerEffect effect,
        GfxObject effectProps,
        SourceSpan fallbackSpan,
        Dictionary<string, SourceSpan>? propertySpans,
        List<GfxDiagnostic> diagnostics)
    {
        var validProps = new HashSet<string>(effect.SupportedProperties, StringComparer.OrdinalIgnoreCase)
        {
            "type",
            "seed"
        };

        foreach (var propName in effectProps.OrderedKeys)
        {
            if (!validProps.Contains(propName))
            {
                var suggestion = PropertySuggestionHelper.GetSuggestionSuffix(propName, validProps);
                var span = propertySpans?.GetValueOrDefault(propName) ?? fallbackSpan;
                diagnostics.Add(new GfxDiagnostic(
                    "FX009",
                    $"Unknown property '{propName}' on effect '{effectType}' on layer '{layerId}'.{suggestion}",
                    span,
                    DiagnosticSeverity.Warning));
            }
        }
    }

    private sealed record LayerAstInfo(
        SourceSpan LayerSpan,
        Dictionary<string, SourceSpan> PropertySpans,
        Dictionary<string, Dictionary<string, SourceSpan>> InlineEffectPropertySpans,
        List<Dictionary<string, SourceSpan>> ArrayEffectPropertySpans);

    private static List<string> ExtractDeclaredLayerOrder(GfxDocument? document)
    {
        var order = new List<string>();
        if (document is null)
        {
            return order;
        }

        var layersProp = document.Properties.LastOrDefault(p => p.Name == "layers");
        if (layersProp?.Value is ObjectBlockExpr layersBlock)
        {
            foreach (var prop in layersBlock.Properties)
            {
                if (!order.Contains(prop.Name, StringComparer.Ordinal))
                {
                    order.Add(prop.Name);
                }
            }
        }

        return order;
    }

    private static Dictionary<string, LayerAstInfo> ExtractLayerAstInfo(GfxDocument? document)
    {
        var map = new Dictionary<string, LayerAstInfo>(StringComparer.Ordinal);
        if (document is null)
        {
            return map;
        }

        var layersProp = document.Properties.LastOrDefault(p => p.Name == "layers");
        if (layersProp?.Value is ObjectBlockExpr layersBlock)
        {
            foreach (var layerProp in layersBlock.Properties)
            {
                var propSpans = new Dictionary<string, SourceSpan>(StringComparer.OrdinalIgnoreCase);
                var inlineSpans = new Dictionary<string, Dictionary<string, SourceSpan>>(StringComparer.OrdinalIgnoreCase);
                var arraySpans = new List<Dictionary<string, SourceSpan>>();

                if (layerProp.Value is ObjectBlockExpr layerBlock)
                {
                    foreach (var prop in layerBlock.Properties)
                    {
                        propSpans[prop.Name] = prop.Span;

                        if (prop.Value is ObjectBlockExpr nestedBlock)
                        {
                            var nestedMap = new Dictionary<string, SourceSpan>(StringComparer.OrdinalIgnoreCase);
                            foreach (var np in nestedBlock.Properties)
                            {
                                nestedMap[np.Name] = np.Span;
                            }

                            inlineSpans[prop.Name] = nestedMap;
                        }
                        else if (prop.Name.Equals("effects", StringComparison.OrdinalIgnoreCase) && prop.Value is ArrayExpr arr)
                        {
                            foreach (var elem in arr.Elements)
                            {
                                var elemMap = new Dictionary<string, SourceSpan>(StringComparer.OrdinalIgnoreCase);
                                if (elem is ObjectBlockExpr elemBlock)
                                {
                                    foreach (var ep in elemBlock.Properties)
                                    {
                                        elemMap[ep.Name] = ep.Span;
                                    }
                                }

                                arraySpans.Add(elemMap);
                            }
                        }
                    }
                }

                map[layerProp.Name] = new LayerAstInfo(layerProp.Span, propSpans, inlineSpans, arraySpans);
            }
        }

        return map;
    }

    private static Dictionary<string, SourceSpan> ExtractLayerSpans(GfxDocument? document)
    {
        var map = new Dictionary<string, SourceSpan>(StringComparer.Ordinal);
        if (document is null)
        {
            return map;
        }

        var layersProp = document.Properties.LastOrDefault(p => p.Name == "layers");
        if (layersProp?.Value is ObjectBlockExpr layersBlock)
        {
            foreach (var prop in layersBlock.Properties)
            {
                map[prop.Name] = prop.Span;
            }
        }

        return map;
    }
}
