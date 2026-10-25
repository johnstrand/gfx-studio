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
    private readonly LayerRendererRegistry _registry;
    private readonly LayerEffectRegistry _effectRegistry;

    public GfxCanvasCompositor(LayerRendererRegistry? registry = null, LayerEffectRegistry? effectRegistry = null)
    {
        _registry = registry ?? LayerRendererRegistry.CreateDefault();
        _effectRegistry = effectRegistry ?? LayerEffectRegistry.CreateDefault();
    }

    public LayerRendererRegistry Registry => _registry;

    /// <summary>Parses, evaluates, and renders the given .gfx source into a composited <see cref="SKBitmap"/>.</summary>
    public CanvasRenderResult RenderSource(
        string source,
        ulong masterSeed = 0,
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

        var seedProvider = new DeterministicSeedProvider(masterSeed);
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

        // 2. Evaluate the layers block
        var layersResult = evaluator.EvaluatePath("layers");
        diagnostics.AddRange(layersResult.Diagnostics);
        if (layersResult.HasErrors || layersResult.Value is null)
        {
            if (!layersResult.HasErrors)
            {
                diagnostics.Add(new GfxDiagnostic("RND001", "Missing required top-level 'layers' block.", documentSpan, DiagnosticSeverity.Error));
            }

            return new CanvasRenderResult(null, canvasDimensions, diagnostics.AsReadOnly());
        }

        if (layersResult.Value is not GfxObject layersObject)
        {
            diagnostics.Add(new GfxDiagnostic("RND001", "Top-level 'layers' property must be an object block.", documentSpan, DiagnosticSeverity.Error));
            return new CanvasRenderResult(null, canvasDimensions, diagnostics.AsReadOnly());
        }

        return CompositeLayers(layersObject, canvasDimensions.Value, context, effectiveSeedProvider, diagnostics);
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
        var layerSpans = ExtractLayerSpans(context.Document);
        var defaultSpan = context.Document?.Span
            ?? new SourceSpan("<memory>", new SourceLocation(1, 1, 0), new SourceLocation(1, 1, 0));

        var canvas = new SKBitmap(new SKImageInfo(
            canvasDimensions.Width,
            canvasDimensions.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        canvas.Erase(SKColors.Transparent);

        var renderedLayers = new Dictionary<string, (SKBitmap Bitmap, Point2D Offset)>(StringComparer.Ordinal);
        try
        {
            foreach (var layerId in layersObject.OrderedKeys)
            {
                if (!layersObject.Properties.TryGetValue(layerId, out var layerValue))
                {
                    continue;
                }

                var layerSpan = layerSpans.GetValueOrDefault(layerId, defaultSpan);
                if (layerValue is not GfxObject layerProps)
                {
                    diagnostics.Add(new GfxDiagnostic(
                        "RND002",
                        $"Layer '{layerId}' must be an object block.",
                        layerSpan,
                        DiagnosticSeverity.Error));
                    continue;
                }

                // Check visible / enabled flag (defaults to true)
                if (!IsLayerVisible(layerProps, layerId, layerSpan, diagnostics))
                {
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

        var layerRandom = effectiveSeedProvider.CreateLayerRandom(context.Run.Number, layerId);
        var layerFrameRandom = effectiveSeedProvider.CreateRandom(context.Run.Number, context.Frame.Number, $"{layerId}:frame");
        var previousRandom = context.Random;
        var previousFrameRandom = context.FrameRandom;
        context.Random = layerRandom;
        context.FrameRandom = layerFrameRandom;

        SKBitmap? rawBitmap;
        try
        {
            if (renderer is EffectAdjustmentLayerRenderer adjustmentRenderer)
            {
                isAdjustmentLayer = true;
                rawBitmap = ExtractCanvasRegion(currentCanvas, offset, layerSize);
                var effectContext = new EffectContext(layerId, layerProps, layerSpan, diagnostics);
                adjustmentRenderer.Effect.Apply(rawBitmap, effectContext);
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
        }

        if (rawBitmap is null)
        {
            return null;
        }

        if (!isAdjustmentLayer)
        {
            ApplyInlineLayerEffects(rawBitmap, layerProps, layerId, layerSpan, diagnostics);
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

    private void ApplyInlineLayerEffects(
        SKBitmap bitmap,
        GfxObject layerProps,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        // 1. outline = { width = ..., color = ... }
        if (layerProps.TryGetProperty("outline", out var outlineVal) && outlineVal is GfxObject outlineObj)
        {
            if (_effectRegistry.TryGetEffect("outline", out var outlineEffect))
            {
                outlineEffect.Apply(bitmap, new EffectContext(layerId, outlineObj, layerSpan, diagnostics));
            }
        }

        // 2. drop_shadow = { offset = ..., blur = ..., color = ... }
        if ((layerProps.TryGetProperty("drop_shadow", out var shadowVal) || layerProps.TryGetProperty("shadow", out shadowVal))
            && shadowVal is GfxObject shadowObj)
        {
            if (_effectRegistry.TryGetEffect("drop_shadow", out var shadowEffect))
            {
                shadowEffect.Apply(bitmap, new EffectContext(layerId, shadowObj, layerSpan, diagnostics));
            }
        }

        // 3. blur / gaussian_blur / box_blur
        if (layerProps.TryGetProperty("blur", out var blurVal)
            || layerProps.TryGetProperty("gaussian_blur", out blurVal)
            || layerProps.TryGetProperty("box_blur", out blurVal))
        {
            var blurProps = CoerceEffectProps(blurVal, "radius");
            if (blurProps is not null && _effectRegistry.TryGetEffect("gaussian_blur", out var blurEffect))
            {
                blurEffect.Apply(bitmap, new EffectContext(layerId, blurProps, layerSpan, diagnostics));
            }
        }

        // 4. threshold
        if (layerProps.TryGetProperty("threshold", out var threshVal))
        {
            var threshProps = CoerceEffectProps(threshVal, "level");
            if (threshProps is not null && _effectRegistry.TryGetEffect("threshold", out var threshEffect))
            {
                threshEffect.Apply(bitmap, new EffectContext(layerId, threshProps, layerSpan, diagnostics));
            }
        }

        // 5. posterize
        if (layerProps.TryGetProperty("posterize", out var postVal))
        {
            var postProps = CoerceEffectProps(postVal, "levels");
            if (postProps is not null && _effectRegistry.TryGetEffect("posterize", out var postEffect))
            {
                postEffect.Apply(bitmap, new EffectContext(layerId, postProps, layerSpan, diagnostics));
            }
        }

        // 6. pixelate
        if (layerProps.TryGetProperty("pixelate", out var pixVal))
        {
            var pixProps = CoerceEffectProps(pixVal, "block_size");
            if (pixProps is not null && _effectRegistry.TryGetEffect("pixelate", out var pixEffect))
            {
                pixEffect.Apply(bitmap, new EffectContext(layerId, pixProps, layerSpan, diagnostics));
            }
        }

        // 7. color_adjust
        if (layerProps.TryGetProperty("color_adjust", out var caVal) && caVal is GfxObject caObj)
        {
            if (_effectRegistry.TryGetEffect("color_adjust", out var caEffect))
            {
                caEffect.Apply(bitmap, new EffectContext(layerId, caObj, layerSpan, diagnostics));
            }
        }

        // 8. effects = [ { type = ..., ... }, ... ]
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

                effect.Apply(bitmap, new EffectContext(layerId, fxObj, layerSpan, diagnostics));
            }
        }
    }

    private static GfxObject? CoerceEffectProps(GfxValue val, string scalarPropName) =>
        val switch
        {
            GfxObject obj => obj,
            GfxNumber num => new GfxObject(new Dictionary<string, GfxValue>(StringComparer.Ordinal) { [scalarPropName] = num }, [scalarPropName]),
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

    private static SKBitmap ApplyLayerTransformIfNeeded(
        SKBitmap source,
        GfxObject layerProps,
        string layerType,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        var shapeHandlesRotationInternally = layerType.Equals("rect", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("polygon", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("star", StringComparison.OrdinalIgnoreCase);

        var rotation = 0.0f;
        if (!shapeHandlesRotationInternally && layerProps.TryGetProperty("rotation", out var rotVal))
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

        var (scaleX, scaleY) = ExtractLayerTransformScale(layerProps, layerType, layerId, layerSpan, diagnostics);
        var flipX = ExtractLayerBoolean(layerProps, "flip_x", layerId, layerSpan, diagnostics);
        var flipY = ExtractLayerBoolean(layerProps, "flip_y", layerId, layerSpan, diagnostics);

        if (Math.Abs(rotation) < 1e-5f
            && Math.Abs(scaleX - 1.0f) < 1e-5f
            && Math.Abs(scaleY - 1.0f) < 1e-5f
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
            && Math.Abs(scaleY - 1.0f) < 1e-5f)
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

        var (anchorX, anchorY) = ExtractLayerAnchor(layerProps, width, height);
        var dest = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        dest.Erase(SKColors.Transparent);

        var finalScaleX = flipX ? -scaleX : scaleX;
        var finalScaleY = flipY ? -scaleY : scaleY;

        using var canvas = new SKCanvas(dest);
        canvas.Translate(anchorX, anchorY);
        canvas.RotateDegrees(rotation);
        canvas.Scale(finalScaleX, finalScaleY);
        canvas.Translate(-anchorX, -anchorY);

        var useNearest = layerProps.TryGetProperty("resample", out var resVal)
            && ((resVal is GfxSymbol s && string.Equals(s.Identifier, "nearest", StringComparison.OrdinalIgnoreCase))
                || (resVal is GfxString str && string.Equals(str.Value, "nearest", StringComparison.OrdinalIgnoreCase)));

        using var paint = new SKPaint
        {
            FilterQuality = useNearest ? SKFilterQuality.None : SKFilterQuality.High,
            IsAntialias = !useNearest
        };
        canvas.DrawBitmap(source, 0, 0, paint);
        canvas.Flush();
        return dest;
    }

    private static (float ScaleX, float ScaleY) ExtractLayerTransformScale(
        GfxObject layerProps,
        string layerType,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        var isNoiseLayer = layerType.Equals("fractal_clouds", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("perlin", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("simplex", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("worley", StringComparison.OrdinalIgnoreCase)
            || layerType.Equals("voronoi", StringComparison.OrdinalIgnoreCase);

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
            return ((float)pt.X, (float)pt.Y);
        }

        diagnostics.Add(new GfxDiagnostic("RND036", $"Invalid 'scale' property on layer '{layerId}'.", layerSpan, DiagnosticSeverity.Error));
        return (1.0f, 1.0f);
    }

    private static bool ExtractLayerBoolean(
        GfxObject layerProps,
        string propName,
        string layerId,
        SourceSpan layerSpan,
        List<GfxDiagnostic> diagnostics)
    {
        if (!layerProps.TryGetProperty(propName, out var val))
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

    private static (float X, float Y) ExtractLayerAnchor(GfxObject layerProps, int width, int height)
    {
        if (!layerProps.TryGetProperty("anchor", out var anchorVal)
            && !layerProps.TryGetProperty("pivot", out anchorVal))
        {
            return (width * 0.5f, height * 0.5f);
        }

        if (anchorVal.TryCoerceToPoint2D(out var pt))
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
        if (layerProps.TryGetProperty("offset", out var offsetVal))
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
