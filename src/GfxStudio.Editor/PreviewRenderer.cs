using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Output;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Parsing.Ast;
using GfxStudio.Core.Randomness;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Editor;

/// <summary>Summary of the resolved .gfx output settings shown in the editor status bar.</summary>
public sealed record PreviewConfig(
    OutputMode Mode,
    int Width,
    int Height,
    int Count,
    double Fps,
    ulong Seed,
    bool UseFrames);

/// <summary>One rendered frame of the preview, encoded to PNG bytes.</summary>
public sealed record PreviewFrame(int FrameIndex, byte[] PngData);

/// <summary>
/// Result of rendering the in-memory preview for a .gfx document.
/// </summary>
public sealed record PreviewResult(
    PreviewConfig? Config,
    IReadOnlyList<PreviewFrame> Frames,
    IReadOnlyList<GfxDiagnostic> Diagnostics,
    string? Error)
{
    public byte[]? FirstFrame => Frames.Count > 0 ? Frames[0].PngData : null;
}

/// <summary>
/// Parses and binds a .gfx source and renders it in memory to PNG bytes.
/// Mirrors <c>GfxEngine.RenderSeriesLike</c>/<c>RenderAnimation</c> context creation so the preview
/// matches the on-disk output exactly, without writing to disk.
/// </summary>
public sealed class PreviewRenderer
{
    private sealed record RenderedPreviewFrame(byte[] PngData, IReadOnlyList<GfxDiagnostic> Diagnostics);

    /// <summary>Preview frame budget for animation modes; frames beyond this are not rendered in the editor.</summary>
    private const int MaxPreviewFrames = 120;

    private readonly GfxCanvasCompositor _compositor = new();
    private readonly OutputConfigBinder _binder = new();

    /// <summary>
    /// Parses <paramref name="source"/> and renders the preview frame(s).
    /// </summary>
    public PreviewResult RenderPreview(string source, string sourceFilePath, ulong? seedOverride = null)
    {
        var parseResult = GfxParser.Parse(source, sourceFilePath);
        if (parseResult.HasErrors || parseResult.Document is null)
        {
            return new PreviewResult(null, Array.Empty<PreviewFrame>(), parseResult.Diagnostics, null);
        }

        var options = new RenderOptions { SeedOverride = seedOverride };
        var bound = _binder.Bind(parseResult.Document, sourceFilePath, options);
        if (bound.Config is null || bound.HasErrors)
        {
            return new PreviewResult(null, Array.Empty<PreviewFrame>(), bound.Diagnostics, null);
        }

        var config = bound.Config;
        var masterSeed = seedOverride ?? config.Seed ?? 0UL;
        var seedProvider = new DeterministicSeedProvider(masterSeed);
        var documentSpan = parseResult.Document.Span;

        var previewConfig = new PreviewConfig(config.Mode, config.Dimensions.Width, config.Dimensions.Height, config.Count, config.Fps, masterSeed, config.UseFrames);
        var allDiagnostics = new List<GfxDiagnostic>(bound.Diagnostics);

        var frames = new List<PreviewFrame>();
        int previewFrameCount = config.UseFrames
            ? Math.Min(config.Count, MaxPreviewFrames)
            : 1;

        for (int frameIndex = 0; frameIndex < previewFrameCount; frameIndex++)
        {
            try
            {
                var renderedFrame = RenderFrame(parseResult.Document, config, seedProvider, frameIndex);
                if (renderedFrame is null)
                {
                    allDiagnostics.Add(new GfxDiagnostic("PREVIEW001", "Frame could not be rendered. See diagnostics for details.", documentSpan, DiagnosticSeverity.Error));
                    break;
                }

                allDiagnostics.AddRange(renderedFrame.Diagnostics);
                frames.Add(new PreviewFrame(frameIndex, renderedFrame.PngData));
            }
            catch (Exception exception)
            {
                allDiagnostics.Add(new GfxDiagnostic("PREVIEW002", $"Frame {frameIndex} rendering failed: {exception.Message}", documentSpan, DiagnosticSeverity.Error));
                break;
            }
        }

        string? error = frames.Count == 0
            ? (parseResult.HasErrors
                ? "Document contains parse errors."
                : (bound.HasErrors || bound.Config is null)
                    ? "Output configuration is invalid."
                    : "No frames could be rendered.")
            : null;

        return new PreviewResult(previewConfig, frames, allDiagnostics, error);
    }

    /// <summary>
    /// Renders a single frame of the document, mirroring the engine's per-frame context creation.
    /// </summary>
    private RenderedPreviewFrame? RenderFrame(GfxDocument document, OutputConfig config, DeterministicSeedProvider seedProvider, int frameIndex)
    {
        int runNumber = config.StartIndex;
        ulong runSeed = seedProvider.DeriveSeed(runNumber, 0, "__run__");

        bool isFrameBased = config.UseFrames;
        int frameNumber = isFrameBased ? frameIndex : 0;
        bool isAnimation = config.Mode is OutputMode.AnimationFrames or OutputMode.AnimationGif;
        int runCount = isAnimation || (config.Mode == OutputMode.ImageMap && isFrameBased)
            ? 1
            : config.Count;

        // FrameContext uses the full config.Count (not the preview cap) so time/progress match actual output.
        int frameCount = isFrameBased ? config.Count : 1;

        var context = new EvaluationContext(
            document,
            new RunContext(runNumber, runCount, runSeed),
            new FrameContext(frameNumber, frameCount, config.Fps),
            seedProvider.CreateLayerRandom(runNumber, "__root__"),
            frameRandom: seedProvider.CreateRandom(runNumber, frameNumber, "__root__:frame"));

        using var renderResult = _compositor.RenderDocument(context, seedProvider);
        if (renderResult.Bitmap is not { } bitmap)
        {
            return null;
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new RenderedPreviewFrame(data.ToArray(), renderResult.Diagnostics);
    }
}
