using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Parsing.Ast;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness;
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

    public GfxCanvasCompositor(LayerRendererRegistry? registry = null)
    {
        _registry = registry ?? LayerRendererRegistry.CreateDefault();
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

            // Extract layer type
            var layerType = ExtractLayerType(layerProps, layerId, layerSpan, diagnostics);
            if (layerType is null)
            {
                continue;
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
                continue;
            }

            // Extract common layer properties: size, offset/position, mix, opacity
            var layerSize = ExtractLayerDimensions(layerProps, canvasDimensions, layerId, layerSpan, diagnostics);
            var offset = ExtractLayerOffset(layerProps, layerId, layerSpan, diagnostics);
            var blendMode = ExtractLayerBlendMode(layerProps, layerId, layerSpan, diagnostics);
            var opacity = ExtractLayerOpacity(layerProps, layerId, layerSpan, diagnostics);

            // Derive isolated deterministic PRNG stream for (MasterSeed, RunNumber, LayerId)
            // Base layer seed stays stable across frames of the same run; FrameRandom varies per frame.
            var layerRandom = effectiveSeedProvider.CreateLayerRandom(context.Run.Number, layerId);
            var layerFrameRandom = effectiveSeedProvider.CreateRandom(context.Run.Number, context.Frame.Number, $"{layerId}:frame");
            var previousRandom = context.Random;
            var previousFrameRandom = context.FrameRandom;
            context.Random = layerRandom;
            context.FrameRandom = layerFrameRandom;

            try
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

                using var layerBitmap = renderer.RenderLayer(renderContext);
                if (layerBitmap is not null)
                {
                    PixelBlender.Composite(canvas, layerBitmap, offset, blendMode, opacity);
                }
            }
            finally
            {
                context.Random = previousRandom;
                context.FrameRandom = previousFrameRandom;
            }
        }

        return new CanvasRenderResult(canvas, canvasDimensions, diagnostics.AsReadOnly());
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
