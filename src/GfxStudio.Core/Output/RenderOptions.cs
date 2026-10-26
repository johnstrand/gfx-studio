namespace GfxStudio.Core.Output;

/// <summary>Per-job configuration and overrides for <see cref="GfxEngine"/>.</summary>
public sealed class RenderOptions
{
    public string? OutputDirectory { get; init; }

    public ulong? SeedOverride { get; init; }

    public OutputMode? ModeOverride { get; init; }

    public int? RunCountOverride { get; init; }

    public int? FrameCountOverride { get; init; }

    public double? FpsOverride { get; init; }

    public int? StartIndexOverride { get; init; }

    public int? MaxDegreeOfParallelism { get; init; }

    public string? AtlasMetadataFilename { get; init; }

    public IReadOnlyDictionary<string, GfxStudio.Core.Evaluation.GfxValue>? VariableOverrides { get; init; }
}
