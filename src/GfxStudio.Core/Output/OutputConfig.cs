using GfxStudio.Core.Primitives;

namespace GfxStudio.Core.Output;

/// <summary>Validated output settings bound from the .gfx output object.</summary>
public sealed record OutputConfig(
    Dimensions Dimensions,
    string Filename,
    OutputMode Mode,
    int Count,
    int StartIndex,
    int Columns,
    int Padding,
    int Spacing,
    ColorRgba Background,
    string? MetadataFilename,
    ulong? Seed,
    bool UseFrames);
