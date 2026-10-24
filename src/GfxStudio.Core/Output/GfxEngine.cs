using System.Text.Json;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Core.Output;

/// <summary>Top-level facade for parsing, rendering, encoding, and writing .gfx outputs.</summary>
public sealed class GfxEngine
{
    private readonly GfxCanvasCompositor _compositor;
    private readonly OutputConfigBinder _binder;
    private readonly SingleImageOutputWriter _imageWriter;

    public GfxEngine(
        GfxCanvasCompositor? compositor = null,
        OutputConfigBinder? binder = null,
        SingleImageOutputWriter? imageWriter = null)
    {
        _compositor = compositor ?? new GfxCanvasCompositor();
        _binder = binder ?? new OutputConfigBinder();
        _imageWriter = imageWriter ?? new SingleImageOutputWriter();
    }

    public RenderJobResult RenderFile(string gfxFilePath, RenderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gfxFilePath);
        var fullPath = Path.GetFullPath(gfxFilePath);
        try
        {
            return RenderSource(File.ReadAllText(fullPath), options, fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var span = new SourceSpan(fullPath, new SourceLocation(1, 1, 0), new SourceLocation(1, 1, 0));
            return Failure(options?.SeedOverride ?? 0, new GfxDiagnostic("OUT013", $"Could not read .gfx file: {exception.Message}", span, DiagnosticSeverity.Error));
        }
    }

    public RenderJobResult RenderSource(string gfxSource, RenderOptions? options = null) =>
        RenderSource(gfxSource, options, "<memory>");

    public RenderJobResult RenderSource(string gfxSource, RenderOptions? options, string sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(gfxSource);
        ArgumentNullException.ThrowIfNull(sourceFilePath);
        options ??= new RenderOptions();

        var parseResult = GfxParser.Parse(gfxSource, sourceFilePath);
        if (parseResult.HasErrors || parseResult.Document is null)
        {
            return new RenderJobResult(options.SeedOverride ?? 0, [], parseResult.Diagnostics);
        }

        var bound = _binder.Bind(parseResult.Document, sourceFilePath, options);
        if (bound.Config is null || bound.HasErrors)
        {
            return new RenderJobResult(options.SeedOverride ?? bound.Config?.Seed ?? 0, [], bound.Diagnostics);
        }

        var config = bound.Config;
        var masterSeed = options.SeedOverride ?? config.Seed ?? 0UL;
        var baseDirectory = ResolveBaseDirectory(sourceFilePath, options.OutputDirectory);
        try
        {
            return config.Mode switch
            {
                OutputMode.Single => RenderSeriesLike(parseResult.Document, config, options, sourceFilePath, baseDirectory, masterSeed, 1, isAtlas: false, isFrameAtlas: false, bound.Diagnostics),
                OutputMode.Series => RenderSeriesLike(parseResult.Document, config, options, sourceFilePath, baseDirectory, masterSeed, config.Count, isAtlas: false, isFrameAtlas: false, bound.Diagnostics),
                OutputMode.ImageMap => RenderSeriesLike(parseResult.Document, config, options, sourceFilePath, baseDirectory, masterSeed, config.Count, isAtlas: true, isFrameAtlas: config.UseFrames, bound.Diagnostics),
                _ => Failure(masterSeed, new GfxDiagnostic("OUT008", $"Output mode '{config.Mode}' is not supported.", parseResult.Document.Span, DiagnosticSeverity.Error))
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or OverflowException or InvalidOperationException)
        {
            return new RenderJobResult(masterSeed, [], bound.Diagnostics.Append(
                new GfxDiagnostic("OUT014", exception.Message, parseResult.Document.Span, DiagnosticSeverity.Error)).ToArray());
        }
    }

    private RenderJobResult RenderSeriesLike(
        Parsing.Ast.GfxDocument document,
        OutputConfig config,
        RenderOptions options,
        string sourceFilePath,
        string baseDirectory,
        ulong masterSeed,
        int renderCount,
        bool isAtlas,
        bool isFrameAtlas,
        IReadOnlyList<GfxDiagnostic> initialDiagnostics)
    {
        var seedProvider = new DeterministicSeedProvider(masterSeed);
        var startIndex = config.StartIndex;
        if ((long)startIndex + renderCount - 1 > int.MaxValue)
        {
            return Failure(masterSeed, new GfxDiagnostic("OUT015", "Run index range exceeds the supported integer range.", document.Span, DiagnosticSeverity.Error));
        }

        var planned = new List<(int RunNumber, string Filename, string FullPath, ulong RunSeed, ulong SpriteSeed)>(renderCount);
        for (var ordinal = 0; ordinal < renderCount; ordinal++)
        {
            var runNumber = isFrameAtlas ? startIndex : startIndex + ordinal;
            var frameNumber = isFrameAtlas ? ordinal : 0;
            var runCount = isFrameAtlas ? 1 : renderCount;
            var runSeed = seedProvider.DeriveSeed(runNumber, 0, "__run__");
            var context = CreateContext(document, runNumber, runCount, runSeed, frameNumber, renderCount, seedProvider);
            var evaluator = new GfxEvaluator(context, seedProvider);
            var filenameResult = evaluator.EvaluatePath("output.filename");
            var diagnostics = filenameResult.Diagnostics;
            if (filenameResult.HasErrors || filenameResult.Value is not GfxString filename || string.IsNullOrWhiteSpace(filename.Value))
            {
                return new RenderJobResult(masterSeed, [], initialDiagnostics.Concat(diagnostics).ToArray());
            }

            var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, filename.Value));
            planned.Add((runNumber, filename.Value, fullPath, runSeed, seedProvider.DeriveSeed(runNumber, frameNumber, "__run__")));
        }

        if (!isAtlas && renderCount > 1)
        {
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var collision = planned.GroupBy(item => item.FullPath, pathComparer).FirstOrDefault(group => group.Count() > 1);
            if (collision is not null)
            {
                return new RenderJobResult(masterSeed, [], initialDiagnostics.Append(new GfxDiagnostic(
                    "OUT016",
                    $"Multiple runs resolve to the same output filename '{collision.Key}'. Use run.number interpolation to avoid overwriting files.",
                    document.Span,
                    DiagnosticSeverity.Error)).ToArray());
            }
        }

        var tiles = new TileResult[renderCount];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism is > 0 ? options.MaxDegreeOfParallelism.Value : -1
        };
        Parallel.For(0, renderCount, parallelOptions, ordinal =>
        {
            var run = planned[ordinal];
            try
            {
                var context = CreateContext(document, run.RunNumber, isFrameAtlas ? 1 : renderCount, run.RunSeed, isFrameAtlas ? ordinal : 0, renderCount, seedProvider);
                var renderResult = _compositor.RenderDocument(context, seedProvider);
                tiles[ordinal] = new TileResult(run.FullPath, run.SpriteSeed, renderResult.Bitmap, renderResult.Diagnostics);
            }
            catch (Exception exception)
            {
                var diagnostic = new GfxDiagnostic("OUT018", $"Run {run.RunNumber} could not be rendered: {exception.Message}", document.Span, DiagnosticSeverity.Error);
                tiles[ordinal] = new TileResult(run.FullPath, run.SpriteSeed, null, [diagnostic]);
            }
        });

        var diagnosticsResult = initialDiagnostics.Concat(tiles.SelectMany(tile => tile.Diagnostics)).ToList();
        if (tiles.Any(tile => tile.Bitmap is null) || diagnosticsResult.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            foreach (var tile in tiles)
            {
                tile.Bitmap?.Dispose();
            }

            return new RenderJobResult(masterSeed, [], diagnosticsResult.AsReadOnly());
        }

        if (!isAtlas)
        {
            var artifacts = new List<GeneratedArtifact>(renderCount);
            try
            {
                foreach (var tile in tiles)
                {
                    var bitmap = tile.Bitmap!;
                    var bytes = _imageWriter.Encode(bitmap, tile.FullPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(tile.FullPath)!);
                    File.WriteAllBytes(tile.FullPath, bytes);
                    artifacts.Add(new GeneratedArtifact(tile.FullPath, bitmap, bytes, config.Dimensions));
                    tile.Bitmap = null;
                }

                return new RenderJobResult(masterSeed, artifacts.AsReadOnly(), diagnosticsResult.AsReadOnly());
            }
            catch
            {
                foreach (var artifact in artifacts)
                {
                    artifact.Dispose();
                }

                throw;
            }
            finally
            {
                foreach (var tile in tiles)
                {
                    tile.Bitmap?.Dispose();
                }
            }
        }

        return BuildAtlas(config, options, baseDirectory, masterSeed, tiles, diagnosticsResult);
    }

    private RenderJobResult BuildAtlas(
        OutputConfig config,
        RenderOptions options,
        string baseDirectory,
        ulong masterSeed,
        TileResult[] tiles,
        List<GfxDiagnostic> diagnostics)
    {
        SKBitmap? atlas = null;
        try
        {
            var rows = (int)Math.Ceiling(tiles.Length / (double)config.Columns);
            var atlasWidthLong = 2L * config.Padding + (long)config.Columns * config.Dimensions.Width + (long)(config.Columns - 1) * config.Spacing;
            var atlasHeightLong = 2L * config.Padding + (long)rows * config.Dimensions.Height + (long)(rows - 1) * config.Spacing;
            if (atlasWidthLong is <= 0 or > int.MaxValue || atlasHeightLong is <= 0 or > int.MaxValue)
            {
                throw new OverflowException("Computed image-map dimensions exceed the supported bitmap size.");
            }

            var atlasWidth = (int)atlasWidthLong;
            var atlasHeight = (int)atlasHeightLong;
            atlas = new SKBitmap(new SKImageInfo(atlasWidth, atlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            atlas.Erase(config.Background.ToSKColor());
            using (var canvas = new SKCanvas(atlas))
            {
                for (var index = 0; index < tiles.Length; index++)
                {
                    var column = index % config.Columns;
                    var row = index / config.Columns;
                    var x = config.Padding + column * (config.Dimensions.Width + config.Spacing);
                    var y = config.Padding + row * (config.Dimensions.Height + config.Spacing);
                    canvas.DrawBitmap(tiles[index].Bitmap!, x, y);
                }

                canvas.Flush();
            }

            var first = tiles[0];
            var filename = Path.GetFileName(first.FullPath);
            var atlasPath = first.FullPath;
            var bytes = _imageWriter.Encode(atlas, atlasPath);
            Directory.CreateDirectory(Path.GetDirectoryName(atlasPath)!);
            File.WriteAllBytes(atlasPath, bytes);

            string? metadataPath = null;
            var metadataFilename = options.AtlasMetadataFilename ?? config.MetadataFilename;
            if (!string.IsNullOrWhiteSpace(metadataFilename))
            {
                metadataPath = Path.GetFullPath(Path.Combine(baseDirectory, metadataFilename));
                var sprites = new ImageMapSpriteMetadata[tiles.Length];
                for (var index = 0; index < tiles.Length; index++)
                {
                    var column = index % config.Columns;
                    var row = index / config.Columns;
                    sprites[index] = new ImageMapSpriteMetadata(
                        index,
                        $"tile_{index:0000}",
                        config.Padding + column * (config.Dimensions.Width + config.Spacing),
                        config.Padding + row * (config.Dimensions.Height + config.Spacing),
                        config.Dimensions.Width,
                        config.Dimensions.Height,
                        tiles[index].Seed);
                }

                var metadata = new ImageMapMetadata(
                    filename,
                    new AtlasDimensions(atlasWidth, atlasHeight),
                    new AtlasDimensions(config.Dimensions.Width, config.Dimensions.Height),
                    masterSeed,
                    sprites);
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
                File.WriteAllText(metadataPath, json);
            }

            var artifact = new GeneratedArtifact(atlasPath, atlas, bytes, new Dimensions(atlasWidth, atlasHeight));
            atlas = null;
            return new RenderJobResult(masterSeed, [artifact], diagnostics.AsReadOnly(), metadataPath);
        }
        finally
        {
            atlas?.Dispose();
            foreach (var tile in tiles)
            {
                tile.Bitmap?.Dispose();
            }
        }
    }

    private static EvaluationContext CreateContext(
        Parsing.Ast.GfxDocument document,
        int runNumber,
        int runCount,
        ulong runSeed,
        int frameNumber,
        int frameCount,
        DeterministicSeedProvider seedProvider) =>
        new(
            document,
            new RunContext(runNumber, runCount, runSeed),
            new FrameContext(frameNumber, frameCount, 15.0),
            seedProvider.CreateRandom(runNumber, 0, "__root__"));

    private static string ResolveBaseDirectory(string sourceFilePath, string? outputDirectory)
    {
        var baseDirectory = outputDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = sourceFilePath == "<memory>"
                ? Environment.CurrentDirectory
                : Path.GetDirectoryName(Path.GetFullPath(sourceFilePath));
        }

        return Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
    }

    private static RenderJobResult Failure(ulong masterSeed, GfxDiagnostic diagnostic) =>
        new(masterSeed, [], [diagnostic]);

    private sealed class TileResult(
        string fullPath,
        ulong seed,
        SKBitmap? bitmap,
        IReadOnlyList<GfxDiagnostic> diagnostics)
    {
        public string FullPath { get; } = fullPath;

        public ulong Seed { get; } = seed;

        public SKBitmap? Bitmap { get; set; } = bitmap;

        public IReadOnlyList<GfxDiagnostic> Diagnostics { get; } = diagnostics;
    }
}
