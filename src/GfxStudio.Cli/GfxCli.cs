using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Documentation;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Formatting;
using GfxStudio.Core.Output;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Parsing.Ast;
using GfxStudio.Core.Randomness;
using GfxStudio.Core.Rendering;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace GfxStudio.Cli;

/// <summary>Builds and runs the gfx command-line application.</summary>
public static partial class GfxCli
{
    public static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Render and inspect GfxStudio .gfx projects.");

        var fileArgument = new Argument<string>("file.gfx", "Path to a .gfx definition file.");
        var outputDirectoryOption = new Option<string?>(["--output-dir", "-o"], "Output directory (defaults to the current directory).");
        var seedOption = new Option<ulong?>(["--seed", "-s"], "Master seed override.");
        var modeOption = new Option<string?>(["--mode", "-m"], "Output mode override.");
        var runsOption = new Option<int?>(["--runs", "-n"], "Run count override.");
        var framesOption = new Option<int?>(["--frames", "-f"], "Animation frame count override.");
        var fpsOption = new Option<double?>("--fps", "Animation frame-rate override.");
        var watchOption = new Option<bool>(["--watch", "-w"], "Re-render this project whenever it or a referenced JavaScript file changes.");
        var jobsOption = new Option<int?>("--jobs", "Maximum parallel render workers.");
        var startIndexOption = new Option<int?>("--start-index", "First run index.");
        var metadataOption = new Option<string?>("--atlas-metadata", "Image-map metadata JSON filename.");

        var render = new Command("render", "Render one or more output artifacts.");
        render.AddArgument(fileArgument);
        render.AddOption(outputDirectoryOption);
        render.AddOption(seedOption);
        render.AddOption(modeOption);
        render.AddOption(runsOption);
        render.AddOption(framesOption);
        render.AddOption(fpsOption);
        render.AddOption(watchOption);
        render.AddOption(jobsOption);
        render.AddOption(startIndexOption);
        render.AddOption(metadataOption);
        render.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            var path = parse.GetValueForArgument(fileArgument);
            var outputDirectory = parse.GetValueForOption(outputDirectoryOption) ?? Environment.CurrentDirectory;
            var modeText = parse.GetValueForOption(modeOption);
            OutputMode? mode = null;
            if (!string.IsNullOrWhiteSpace(modeText))
            {
                if (!TryParseOutputMode(modeText, out var parsedMode))
                {
                    Console.Error.WriteLine($"Unsupported output mode '{modeText}'. Use single, series, image_map, animation_frames, or animation_gif.");
                    context.ExitCode = 2;
                    return;
                }

                mode = parsedMode;
            }

            var options = new RenderOptions
            {
                OutputDirectory = outputDirectory,
                SeedOverride = parse.GetValueForOption(seedOption),
                ModeOverride = mode,
                RunCountOverride = parse.GetValueForOption(runsOption),
                FrameCountOverride = parse.GetValueForOption(framesOption),
                FpsOverride = parse.GetValueForOption(fpsOption),
                MaxDegreeOfParallelism = parse.GetValueForOption(jobsOption),
                StartIndexOverride = parse.GetValueForOption(startIndexOption),
                AtlasMetadataFilename = parse.GetValueForOption(metadataOption)
            };

            var watch = parse.GetValueForOption(watchOption);
            context.ExitCode = watch
                ? await WatchAsync(path, options, context.GetCancellationToken()).ConfigureAwait(false)
                : RenderOnce(path, options);
        });

        var validate = new Command("validate", "Parse and validate .gfx expressions and layer properties without writing output images.");
        validate.AddArgument(fileArgument);
        validate.AddOption(seedOption);
        validate.SetHandler(context =>
        {
            context.ExitCode = ValidateFile(
                context.ParseResult.GetValueForArgument(fileArgument),
                context.ParseResult.GetValueForOption(seedOption));
        });

        var inspect = new Command("inspect", "Print the resolved output configuration and ordered layers.");
        var inspectJsonOption = new Option<bool>("--json", "Write a machine-readable JSON summary.");
        inspect.AddArgument(fileArgument);
        inspect.AddOption(seedOption);
        inspect.AddOption(inspectJsonOption);
        inspect.SetHandler(context =>
        {
            context.ExitCode = InspectFile(
                context.ParseResult.GetValueForArgument(fileArgument),
                context.ParseResult.GetValueForOption(seedOption),
                context.ParseResult.GetValueForOption(inspectJsonOption));
        });

        var format = new Command("format", "Format a .gfx file in-place using GfxFormatter (or verify formatting with --check).");
        var checkFormatOption = new Option<bool>("--check", "Verify that the file is already formatted without writing changes.");
        format.AddArgument(fileArgument);
        format.AddOption(checkFormatOption);
        format.SetHandler(context =>
        {
            context.ExitCode = FormatFile(
                context.ParseResult.GetValueForArgument(fileArgument),
                context.ParseResult.GetValueForOption(checkFormatOption));
        });

        var wiki = new Command("wiki", "Generate GitHub Wiki Markdown reference pages from registered layer and effect metadata.");
        var wikiOutputOption = new Option<string?>(["--output", "--output-dir", "-o"], "Directory where Wiki Markdown pages should be written (defaults to ./artifacts/wiki).");
        wiki.AddOption(wikiOutputOption);
        wiki.SetHandler(context =>
        {
            var outDir = context.ParseResult.GetValueForOption(wikiOutputOption) ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "wiki");
            context.ExitCode = GenerateWiki(outDir);
        });

        root.AddCommand(render);
        root.AddCommand(validate);
        root.AddCommand(inspect);
        root.AddCommand(format);
        root.AddCommand(wiki);
        return root;
    }

    public static Task<int> InvokeAsync(string[] args) => BuildRootCommand().InvokeAsync(args);

    private static int FormatFile(string filePath, bool checkOnly)
    {
        if (!TryReadSource(filePath, out var fullPath, out var source))
        {
            return 1;
        }

        var formatResult = GfxFormatter.Format(source, fullPath);
        if (formatResult.HasErrors)
        {
            WriteDiagnostics(formatResult.Diagnostics, source, KnownNames(source, fullPath));
            return 1;
        }

        if (checkOnly)
        {
            if (formatResult.Changed)
            {
                Console.Error.WriteLine($"Not formatted: {fullPath}");
                return 1;
            }

            Console.WriteLine($"Formatted: {fullPath}");
            return 0;
        }

        if (formatResult.Changed)
        {
            try
            {
                File.WriteAllText(fullPath, formatResult.FormattedText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"error[CLI001]: Could not write '{fullPath}': {exception.Message}");
                return 1;
            }
        }

        Console.WriteLine($"Formatted: {fullPath}");
        return 0;
    }

    private static int GenerateWiki(string outputDirectory)
    {
        try
        {
            var fullOutDir = Path.GetFullPath(outputDirectory);
            var files = GfxWikiGenerator.WriteToDirectory(fullOutDir);
            Console.WriteLine($"Generated {files.Count} Wiki pages in '{fullOutDir}':");
            foreach (var file in files)
            {
                Console.WriteLine($"  {Path.GetFileName(file)}");
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error[CLI001]: Could not write Wiki pages: {exception.Message}");
            return 1;
        }
    }

    private static int RenderOnce(string filePath, RenderOptions options)
    {
        var fullPath = Path.GetFullPath(filePath);
        string source;
        try
        {
            source = File.ReadAllText(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error[CLI001]: Could not read '{fullPath}': {exception.Message}");
            return 1;
        }

        var stopwatch = Stopwatch.StartNew();
        using var result = new GfxEngine().RenderFile(fullPath, options);
        stopwatch.Stop();
        var knownNames = KnownNames(source, fullPath);
        WriteDiagnostics(result.Diagnostics, source, knownNames);
        Console.WriteLine($"Master seed: {result.MasterSeed}");
        foreach (var artifact in result.Artifacts)
        {
            Console.WriteLine($"Output: {artifact.FilePath}");
        }

        if (result.MetadataFilePath is not null)
        {
            Console.WriteLine($"Metadata: {result.MetadataFilePath}");
        }

        Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        return result.HasErrors ? 1 : 0;
    }

    private static int ValidateFile(string filePath, ulong? seedOverride)
    {
        if (!TryReadSource(filePath, out var fullPath, out var source))
        {
            return 1;
        }

        var parseResult = GfxParser.Parse(source, fullPath);
        if (parseResult.HasErrors || parseResult.Document is null)
        {
            WriteDiagnostics(parseResult.Diagnostics, source, KnownNames(source, fullPath));
            return 1;
        }

        var options = new RenderOptions { SeedOverride = seedOverride };
        var binder = new OutputConfigBinder().Bind(parseResult.Document, fullPath, options);
        var masterSeed = seedOverride ?? binder.Config?.Seed ?? 0UL;
        var seedProvider = new DeterministicSeedProvider(masterSeed);
        var context = CreateEvaluationContext(parseResult.Document, seedProvider, 0, 1, 0, 1, binder.Config?.Fps ?? 15.0);
        var evaluation = new GfxEvaluator(context, seedProvider).EvaluateDocument();
        var diagnostics = parseResult.Diagnostics.Concat(binder.Diagnostics).Concat(evaluation.Diagnostics).ToList();

        using (var validationRender = new GfxCanvasCompositor().RenderDocument(context, seedProvider))
        {
            diagnostics.AddRange(validationRender.Diagnostics);
        }

        diagnostics = diagnostics.Distinct().ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            WriteDiagnostics(diagnostics, source, KnownNames(source, fullPath));
            return 1;
        }

        Console.WriteLine($"Valid: {fullPath}");
        return 0;
    }

    private static int InspectFile(string filePath, ulong? seedOverride, bool json)
    {
        if (!TryReadSource(filePath, out var fullPath, out var source))
        {
            return 1;
        }

        var parseResult = GfxParser.Parse(source, fullPath);
        if (parseResult.HasErrors || parseResult.Document is null)
        {
            WriteDiagnostics(parseResult.Diagnostics, source, KnownNames(source, fullPath));
            return 1;
        }

        var options = new RenderOptions { SeedOverride = seedOverride };
        var bindResult = new OutputConfigBinder().Bind(parseResult.Document, fullPath, options);
        if (bindResult.Config is null || bindResult.HasErrors)
        {
            WriteDiagnostics(bindResult.Diagnostics, source, KnownNames(source, fullPath));
            return 1;
        }

        var config = bindResult.Config;
        var masterSeed = seedOverride ?? config.Seed ?? 0UL;
        var seedProvider = new DeterministicSeedProvider(masterSeed);
        var evaluation = new GfxEvaluator(
            CreateEvaluationContext(parseResult.Document, seedProvider, 0, config.Count, 0, config.Count, config.Fps),
            seedProvider).EvaluateDocument();
        if (evaluation.HasErrors || evaluation.Value is null)
        {
            WriteDiagnostics(evaluation.Diagnostics, source, KnownNames(source, fullPath));
            return 1;
        }

        var layers = evaluation.Value.Properties.TryGetValue("layers", out var layersValue) && layersValue is GfxObject layersObject
            ? layersObject.OrderedKeys.Select(key => new LayerSummary(
                key,
                layersObject.Properties.TryGetValue(key, out var layerValue) && layerValue is GfxObject properties
                    ? properties.OrderedKeys.Where(properties.Properties.ContainsKey).ToDictionary(propertyName => propertyName, propertyName => ToSerializableValue(properties.Properties[propertyName]))
                    : new Dictionary<string, object?>())).ToArray()
            : [];
        var summary = new InspectionSummary(
            masterSeed,
            new OutputSummary(
                FormatMode(config.Mode),
                config.Filename,
                config.Dimensions.Width,
                config.Dimensions.Height,
                config.Count,
                config.StartIndex,
                config.Columns,
                config.Padding,
                config.Spacing,
                config.Background,
                config.MetadataFilename,
                config.UseFrames,
                config.Fps,
                config.FrameDelayMs,
                config.Loop,
                config.Dither),
            layers);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"Master seed: {summary.MasterSeed}");
        Console.WriteLine($"Output: {summary.Output.Mode} {summary.Output.Width}x{summary.Output.Height} -> {summary.Output.Filename}");
        Console.WriteLine($"Count: {summary.Output.Count}; start index: {summary.Output.StartIndex}; FPS: {summary.Output.Fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Image map: {summary.Output.Columns} columns, {summary.Output.Padding}px padding, {summary.Output.Spacing}px spacing");
        Console.WriteLine("Layers:");
        foreach (var layer in layers)
        {
            var typeName = layer.Properties.TryGetValue("type", out var type) ? type?.ToString() : "unknown";
            Console.WriteLine($"  {layer.Id} ({typeName})");
            foreach (var (name, value) in layer.Properties)
            {
                if (name != "type")
                {
                    Console.WriteLine($"    {name}: {JsonSerializer.Serialize(value)}");
                }
            }
        }

        return 0;
    }

    private static async Task<int> WatchAsync(string filePath, RenderOptions options, CancellationToken invocationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(invocationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var source = File.ReadAllText(fullPath);
            var paths = GetWatchedPaths(fullPath, source).ToArray();
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var watchedPaths = paths.ToHashSet(pathComparer);
            var changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            using var watcherCollection = new WatcherCollection(CreateWatchers(paths, watchedPaths, changes.Writer, pathComparer));
            Console.WriteLine($"Watching {string.Join(", ", paths)}. Press Ctrl+C to stop.");
            _ = RenderOnce(fullPath, options);
            while (!cancellation.IsCancellationRequested)
            {
                await changes.Reader.ReadAsync(cancellation.Token).ConfigureAwait(false);
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150), cancellation.Token).ConfigureAwait(false);
                    if (!changes.Reader.TryRead(out _))
                    {
                        break;
                    }

                    while (changes.Reader.TryRead(out _))
                    {
                        // Restart the quiet-period timer after coalescing this burst of file events.
                    }
                }

                try
                {
                    source = File.ReadAllText(fullPath);
                    var updatedPaths = GetWatchedPaths(fullPath, source).ToArray();
                    var updatedWatchedPaths = updatedPaths.ToHashSet(pathComparer);
                    if (!updatedWatchedPaths.SetEquals(watchedPaths))
                    {
                        paths = updatedPaths;
                        watchedPaths = updatedWatchedPaths;
                        watcherCollection.Replace(CreateWatchers(paths, watchedPaths, changes.Writer, pathComparer));
                    }
                }
                catch (IOException exception)
                {
                    Console.Error.WriteLine($"warning[CLI002]: Could not reread watched source: {exception.Message}");
                }

                _ = RenderOnce(fullPath, options);
            }

            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error[CLI001]: Could not watch '{fullPath}': {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static List<FileSystemWatcher> CreateWatchers(
        IReadOnlyList<string> paths,
        HashSet<string> watchedPaths,
        ChannelWriter<bool> changes,
        StringComparer pathComparer)
    {
        var watchers = new List<FileSystemWatcher>();
        foreach (var directory in paths.Select(Path.GetDirectoryName).Where(value => !string.IsNullOrEmpty(value)).Distinct(pathComparer))
        {
            var watcher = new FileSystemWatcher(directory!)
            {
                Filter = "*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false
            };
            FileSystemEventHandler onChange = (_, eventArgs) =>
            {
                if (watchedPaths.Contains(Path.GetFullPath(eventArgs.FullPath)))
                {
                    changes.TryWrite(true);
                }
            };
            RenamedEventHandler onRename = (_, eventArgs) =>
            {
                if (watchedPaths.Contains(Path.GetFullPath(eventArgs.FullPath)) || watchedPaths.Contains(Path.GetFullPath(eventArgs.OldFullPath)))
                {
                    changes.TryWrite(true);
                }
            };
            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Renamed += onRename;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }

        return watchers;
    }

    private static bool TryReadSource(string filePath, out string fullPath, out string source)
    {
        fullPath = Path.GetFullPath(filePath);
        try
        {
            source = File.ReadAllText(fullPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            source = string.Empty;
            Console.Error.WriteLine($"error[CLI001]: Could not read '{fullPath}': {exception.Message}");
            return false;
        }
    }

    private static void WriteDiagnostics(IEnumerable<GfxDiagnostic> diagnostics, string source, IEnumerable<string> knownNames)
    {
        foreach (var diagnostic in diagnostics)
        {
            Console.Error.WriteLine(DiagnosticConsoleFormatter.Format(diagnostic, source, knownNames));
        }
    }

    private static IEnumerable<string> KnownNames(string source, string sourcePath)
    {
        var parse = GfxParser.Parse(source, sourcePath);
        var context = parse.Document is null ? new EvaluationContext() : new EvaluationContext(parse.Document);
        return BuiltInConstants.RootConstants.Keys
            .Concat(context.Functions.Keys)
            .Concat(BuiltInConstants.NamedColors.Properties.Keys)
            .Concat(BuiltInConstants.Align.Properties.Keys)
            .Concat(parse.Document?.Properties.Select(property => property.Name) ?? [])
            .Concat(LayerRendererRegistry.CreateDefault().RegisteredLayerTypes);
    }

    private static EvaluationContext CreateEvaluationContext(
        GfxDocument document,
        DeterministicSeedProvider seedProvider,
        int runNumber,
        int runCount,
        int frameNumber,
        int frameCount,
        double fps) =>
        new(
            document,
            new RunContext(runNumber, runCount, seedProvider.DeriveSeed(runNumber, 0, "__run__")),
            new FrameContext(frameNumber, frameCount, fps),
            seedProvider.CreateLayerRandom(runNumber, "__root__"),
            frameRandom: seedProvider.CreateRandom(runNumber, frameNumber, "__root__:frame"));

    private static object? ToSerializableValue(GfxValue value) => value switch
    {
        GfxNumber number => number.Value,
        GfxString text => text.Value,
        GfxBoolean boolean => boolean.Value,
        GfxSymbol symbol => symbol.Identifier,
        GfxColor color => new { color.Color.R, color.Color.G, color.Color.B, color.Color.A },
        GfxDimensions dimensions => new { dimensions.Dimensions.Width, dimensions.Dimensions.Height },
        GfxColorRamp ramp => ramp.Ramp.Stops.Select(stop => new { stop.Position, stop.Color.R, stop.Color.G, stop.Color.B, stop.Color.A }).ToArray(),
        GfxArray array => array.Items.Select(ToSerializableValue).ToArray(),
        GfxObject obj => obj.OrderedKeys.Where(obj.Properties.ContainsKey).ToDictionary(key => key, key => ToSerializableValue(obj.Properties[key])),
        GfxFunctionRef function => $"<function {function.Callable.Name}>",
        _ => value.ToDisplayString()
    };

    private static bool TryParseOutputMode(string value, out OutputMode mode)
    {
        mode = value.Trim().ToLowerInvariant() switch
        {
            "single" => OutputMode.Single,
            "series" => OutputMode.Series,
            "image_map" or "imagemap" => OutputMode.ImageMap,
            "animation_frames" or "animationframes" or "frames" => OutputMode.AnimationFrames,
            "animation_gif" or "animationgif" or "gif" => OutputMode.AnimationGif,
            _ => (OutputMode)(-1)
        };
        return Enum.IsDefined(mode);
    }

    private static string FormatMode(OutputMode mode) => mode switch
    {
        OutputMode.ImageMap => "image_map",
        OutputMode.AnimationFrames => "animation_frames",
        OutputMode.AnimationGif => "animation_gif",
        _ => mode.ToString().ToLowerInvariant()
    };

    private static IEnumerable<string> GetWatchedPaths(string gfxPath, string source)
    {
        var fullGfxPath = Path.GetFullPath(gfxPath);
        yield return fullGfxPath;
        var baseDirectory = Path.GetDirectoryName(fullGfxPath) ?? Environment.CurrentDirectory;
        foreach (Match match in JavaScriptReferenceRegex().Matches(source))
        {
            var referencedPath = Path.GetFullPath(Path.Combine(baseDirectory, match.Groups[1].Value));
            if (Directory.Exists(Path.GetDirectoryName(referencedPath)))
            {
                yield return referencedPath;
            }
        }
    }

    [GeneratedRegex("[\\\"']([^\\\"']+\\.js)[\\\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaScriptReferenceRegex();

    private sealed record OutputSummary(
        string Mode,
        string Filename,
        int Width,
        int Height,
        int Count,
        int StartIndex,
        int Columns,
        int Padding,
        int Spacing,
        GfxStudio.Core.Primitives.ColorRgba Background,
        string? MetadataFilename,
        bool UseFrames,
        double Fps,
        int? FrameDelayMs,
        int Loop,
        bool Dither);

    private sealed record LayerSummary(string Id, IReadOnlyDictionary<string, object?> Properties);

    private sealed record InspectionSummary(ulong MasterSeed, OutputSummary Output, IReadOnlyList<LayerSummary> Layers);

    private sealed class WatcherCollection(List<FileSystemWatcher> watchers) : IDisposable
    {
        public void Replace(List<FileSystemWatcher> newWatchers)
        {
            DisposeWatchers(watchers);
            watchers = newWatchers;
        }

        public void Dispose()
        {
            DisposeWatchers(watchers);
        }

        private static void DisposeWatchers(IEnumerable<FileSystemWatcher> items)
        {
            foreach (var watcher in items)
            {
                watcher.Dispose();
            }
        }
    }
}
