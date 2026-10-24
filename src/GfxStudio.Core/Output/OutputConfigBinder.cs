using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Parsing.Ast;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Core.Output;

/// <summary>Validates and extracts output settings from a parsed .gfx document.</summary>
public sealed class OutputConfigBinder
{
    public OutputConfigBindResult Bind(
        GfxDocument document,
        string sourceFilePath = "<memory>",
        RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new RenderOptions();

        var span = document.Span;
        var diagnostics = new List<GfxDiagnostic>();
        var initialRun = new RunContext(0, 1, 0);
        var evaluator = new GfxEvaluator(new EvaluationContext(document, initialRun, new FrameContext(0, 1, 15.0)));

        var dimensionsValue = EvaluateIfDeclared(document, evaluator, "output.dimensions");
        if (dimensionsValue is null || !dimensionsValue.TryCoerceToDimensions(out var dimensions))
        {
            diagnostics.Add(Error("OUT001", "Missing or invalid required 'output.dimensions'; expected positive integer width and height.", span));
        }

        var filenameValue = EvaluateIfDeclared(document, evaluator, "output.filename");
        if (filenameValue is not GfxString filename || string.IsNullOrWhiteSpace(filename.Value))
        {
            diagnostics.Add(Error("OUT002", "Missing or invalid required 'output.filename'; expected a non-empty string.", span));
        }

        var mode = OutputMode.Single;
        var modeValue = EvaluateIfDeclared(document, evaluator, "output.mode");
        if (modeValue is not null)
        {
            var modeName = modeValue switch
            {
                GfxSymbol symbol => symbol.Identifier,
                GfxString text => text.Value,
                _ => string.Empty
            };
            if (!TryParseMode(modeName, out mode))
            {
                diagnostics.Add(Error("OUT003", $"Unsupported output mode '{modeValue.ToDisplayString()}'. Supported modes are single, series, and image_map.", span));
            }
        }

        if (options.ModeOverride is { } modeOverride)
        {
            mode = modeOverride;
        }

        var startIndex = ReadInteger(document, evaluator, "output.start_index", 0, 0, int.MaxValue, diagnostics, span);
        var countOrRunsDeclared = IsDeclared(document, "output.count") || IsDeclared(document, "output.runs");
        var framesDeclared = IsDeclared(document, "output.frames");
        var useFrames = mode == OutputMode.ImageMap && (options.FrameCountOverride is not null || framesDeclared && !countOrRunsDeclared);
        if (useFrames && countOrRunsDeclared)
        {
            diagnostics.Add(Error("OUT017", "Specify either 'output.frames' or 'output.count'/'output.runs', not both.", span));
        }

        var configuredCount = useFrames
            ? ReadInteger(document, evaluator, "output.frames", 1, 1, int.MaxValue, diagnostics, span)
            : ReadAliasedInteger(document, evaluator, "output.count", "output.runs", 1, 1, int.MaxValue, diagnostics, span);
        var count = options.RunCountOverride ?? configuredCount;
        if (useFrames && options.FrameCountOverride is { } frameCount)
        {
            count = frameCount;
        }
        if (count < 1)
        {
            diagnostics.Add(Error("OUT004", "Run count must be a positive integer.", span));
            count = 1;
        }

        if (options.StartIndexOverride is { } startOverride)
        {
            if (startOverride < 0)
            {
                diagnostics.Add(Error("OUT005", "Start index must be a non-negative integer.", span));
            }
            else
            {
                startIndex = startOverride;
            }
        }

        if (options.ModeOverride is null && mode == OutputMode.Single && options.RunCountOverride is > 1)
        {
            mode = OutputMode.Series;
        }
        else if (options.ModeOverride is null && mode == OutputMode.Single && configuredCount > 1)
        {
            mode = OutputMode.Series;
        }

        var columns = ReadInteger(document, evaluator, "output.columns", (int)Math.Ceiling(Math.Sqrt(count)), 1, int.MaxValue, diagnostics, span);
        var padding = ReadInteger(document, evaluator, "output.padding", 0, 0, int.MaxValue, diagnostics, span);
        var spacing = ReadInteger(document, evaluator, "output.spacing", 0, 0, int.MaxValue, diagnostics, span);
        var background = new ColorRgba(0, 0, 0, 0);
        var backgroundValue = EvaluateIfDeclared(document, evaluator, "output.background");
        if (backgroundValue is not null)
        {
            if (backgroundValue is GfxColor gfxColor)
            {
                background = gfxColor.Color;
            }
            else
            {
                diagnostics.Add(Error("OUT006", "Property 'output.background' must be a color.", span));
            }
        }

        var metadataFilename = options.AtlasMetadataFilename;
        if (metadataFilename is null)
        {
            var metadataValue = EvaluateIfDeclared(document, evaluator, "output.metadata_filename");
            if (metadataValue is GfxString metadataString && !string.IsNullOrWhiteSpace(metadataString.Value))
            {
                metadataFilename = metadataString.Value;
            }
            else if (metadataValue is not null)
            {
                diagnostics.Add(Error("OUT007", "Property 'output.metadata_filename' must be a non-empty string.", span));
            }
        }

        var seed = ReadSeed(document, evaluator, diagnostics, span);
        diagnostics.AddRange(evaluator.Diagnostics);
        diagnostics = diagnostics.Distinct().ToList();

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || dimensionsValue is null || !dimensionsValue.TryCoerceToDimensions(out dimensions)
            || filenameValue is not GfxString validFilename)
        {
            return new OutputConfigBindResult(null, diagnostics.AsReadOnly());
        }

        if (mode is OutputMode.AnimationFrames or OutputMode.AnimationGif)
        {
            diagnostics.Add(Error("OUT008", $"Output mode '{mode}' is not implemented.", span));
            return new OutputConfigBindResult(null, diagnostics.AsReadOnly());
        }

        return new OutputConfigBindResult(
            new OutputConfig(dimensions, validFilename.Value, mode, count, startIndex, columns, padding, spacing, background, metadataFilename, seed, useFrames),
            diagnostics.AsReadOnly());
    }

    private static int ReadAliasedInteger(
        GfxDocument document,
        GfxEvaluator evaluator,
        string primaryPath,
        string aliasPath,
        int defaultValue,
        int minimum,
        int maximum,
        List<GfxDiagnostic> diagnostics,
        SourceSpan span)
    {
        var primary = EvaluateIfDeclared(document, evaluator, primaryPath);
        var alias = EvaluateIfDeclared(document, evaluator, aliasPath);
        if (primary is not null && alias is not null)
        {
            diagnostics.Add(Error("OUT009", $"Specify only one of '{primaryPath}' and '{aliasPath}'.", span));
        }

        return ReadIntegerValue(primary ?? alias, primaryPath, defaultValue, minimum, maximum, diagnostics, span);
    }

    private static int ReadInteger(
        GfxDocument document,
        GfxEvaluator evaluator,
        string path,
        int defaultValue,
        int minimum,
        int maximum,
        List<GfxDiagnostic> diagnostics,
        SourceSpan span) => ReadIntegerValue(EvaluateIfDeclared(document, evaluator, path), path, defaultValue, minimum, maximum, diagnostics, span);

    private static int ReadIntegerValue(
        GfxValue? value,
        string path,
        int defaultValue,
        int minimum,
        int maximum,
        List<GfxDiagnostic> diagnostics,
        SourceSpan span)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (value is GfxNumber number
            && double.IsFinite(number.Value)
            && number.Value == Math.Truncate(number.Value)
            && number.Value >= minimum
            && number.Value <= maximum)
        {
            return (int)number.Value;
        }

        diagnostics.Add(Error("OUT010", $"Property '{path}' must be an integer between {minimum} and {maximum}.", span));
        return defaultValue;
    }

    private static ulong? ReadSeed(GfxDocument document, GfxEvaluator evaluator, List<GfxDiagnostic> diagnostics, SourceSpan span)
    {
        var outputSeed = EvaluateIfDeclared(document, evaluator, "output.seed");
        var topLevelSeed = EvaluateIfDeclared(document, evaluator, "seed");
        if (outputSeed is not null && topLevelSeed is not null)
        {
            diagnostics.Add(Error("OUT011", "Specify seed either at the top level or inside output, not both.", span));
        }

        var value = outputSeed ?? topLevelSeed;
        if (value is null)
        {
            return null;
        }

        if (value is GfxNumber number && double.IsFinite(number.Value)
            && number.Value >= 0 && number.Value < 18446744073709551616.0
            && number.Value == Math.Truncate(number.Value))
        {
            return (ulong)number.Value;
        }

        diagnostics.Add(Error("OUT012", "Seed must be a non-negative integer in the unsigned 64-bit range.", span));
        return null;
    }

    private static bool TryParseMode(string name, out OutputMode mode)
    {
        mode = name.ToLowerInvariant() switch
        {
            "single" => OutputMode.Single,
            "series" => OutputMode.Series,
            "image_map" or "imagemap" => OutputMode.ImageMap,
            _ => (OutputMode)(-1)
        };
        return Enum.IsDefined(mode);
    }

    private static GfxDiagnostic Error(string code, string message, SourceSpan span) =>
        new(code, message, span, DiagnosticSeverity.Error);

    private static GfxValue? EvaluateIfDeclared(GfxDocument document, GfxEvaluator evaluator, string path)
    {
        if (!IsDeclared(document, path))
        {
            return null;
        }

        var result = evaluator.EvaluatePath(path);
        return result.Value;
    }

    private static bool IsDeclared(GfxDocument document, string path)
    {
        var segments = path.Split('.');
        var assignment = document.Properties.LastOrDefault(property => property.Name == segments[0]);
        if (assignment is null)
        {
            return false;
        }

        GfxExpr expression = assignment.Value;
        for (var index = 1; index < segments.Length; index++)
        {
            if (expression is not ObjectBlockExpr block)
            {
                return false;
            }

            var child = block.Properties.LastOrDefault(property => property.Name == segments[index]);
            if (child is null)
            {
                return false;
            }

            expression = child.Value;
        }

        return true;
    }
}
