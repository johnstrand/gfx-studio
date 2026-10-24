using GfxStudio.Core.Output;

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    PrintUsage();
    return 0;
}

if (!string.Equals(args[0], "render", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
    PrintUsage();
    return 2;
}

string? inputPath = null;
string? outputDirectory = null;
string? atlasMetadata = null;
ulong? seed = null;
int? runs = null;
int? frames = null;
int? startIndex = null;
int? jobs = null;
OutputMode? mode = null;

for (var index = 1; index < args.Length; index++)
{
    var argument = args[index];
    if (!argument.StartsWith("-", StringComparison.Ordinal))
    {
        if (inputPath is not null)
        {
            Console.Error.WriteLine("Only one .gfx input file may be specified.");
            return 2;
        }

        inputPath = argument;
        continue;
    }

    if (index + 1 >= args.Length)
    {
        Console.Error.WriteLine($"Option '{argument}' requires a value.");
        return 2;
    }

    var value = args[++index];
    switch (argument)
    {
        case "--output":
        case "-o":
            outputDirectory = value;
            break;
        case "--seed":
            if (!ulong.TryParse(value, out var parsedSeed))
            {
                Console.Error.WriteLine($"Invalid unsigned seed '{value}'.");
                return 2;
            }

            seed = parsedSeed;
            break;
        case "--runs":
        case "--count":
            if (!int.TryParse(value, out var parsedRuns) || parsedRuns < 1)
            {
                Console.Error.WriteLine($"Invalid run count '{value}'; expected a positive integer.");
                return 2;
            }

            runs = parsedRuns;
            break;
        case "--frames":
            if (!int.TryParse(value, out var parsedFrames) || parsedFrames < 1)
            {
                Console.Error.WriteLine($"Invalid frame count '{value}'; expected a positive integer.");
                return 2;
            }

            frames = parsedFrames;
            break;
        case "--start-index":
            if (!int.TryParse(value, out var parsedStart) || parsedStart < 0)
            {
                Console.Error.WriteLine($"Invalid start index '{value}'; expected a non-negative integer.");
                return 2;
            }

            startIndex = parsedStart;
            break;
        case "--jobs":
            if (!int.TryParse(value, out var parsedJobs) || parsedJobs < 1)
            {
                Console.Error.WriteLine($"Invalid job count '{value}'; expected a positive integer.");
                return 2;
            }

            jobs = parsedJobs;
            break;
        case "--mode":
            if (!TryParseMode(value, out var parsedMode))
            {
                Console.Error.WriteLine($"Unsupported output mode '{value}'. Use single, series, or image_map.");
                return 2;
            }

            mode = parsedMode;
            break;
        case "--atlas-metadata":
            atlasMetadata = value;
            break;
        default:
            Console.Error.WriteLine($"Unknown option '{argument}'.");
            return 2;
    }
}

if (inputPath is null)
{
    Console.Error.WriteLine("A .gfx input file is required.");
    PrintUsage();
    return 2;
}

using var result = new GfxEngine().RenderFile(inputPath, new RenderOptions
{
    OutputDirectory = outputDirectory,
    SeedOverride = seed,
    ModeOverride = mode,
    RunCountOverride = runs,
    FrameCountOverride = frames,
    StartIndexOverride = startIndex,
    MaxDegreeOfParallelism = jobs,
    AtlasMetadataFilename = atlasMetadata
});

foreach (var diagnostic in result.Diagnostics)
{
    Console.Error.WriteLine(diagnostic);
}

foreach (var artifact in result.Artifacts)
{
    Console.WriteLine(artifact.FilePath);
}

if (result.MetadataFilePath is not null)
{
    Console.WriteLine(result.MetadataFilePath);
}

return result.HasErrors ? 1 : 0;

static bool TryParseMode(string value, out OutputMode mode)
{
    mode = value.ToLowerInvariant() switch
    {
        "single" => OutputMode.Single,
        "series" => OutputMode.Series,
        "image_map" or "imagemap" => OutputMode.ImageMap,
        _ => (OutputMode)(-1)
    };
    return Enum.IsDefined(mode);
}

static void PrintUsage()
{
    Console.WriteLine("Usage: gfx render <file.gfx> [--runs <count>] [--frames <count>] [--seed <uint64>]");
    Console.WriteLine("                  [--mode <single|series|image_map>] [--start-index <index>]");
    Console.WriteLine("                  [--output <directory>] [--jobs <count>] [--atlas-metadata <file.json>]");
}
