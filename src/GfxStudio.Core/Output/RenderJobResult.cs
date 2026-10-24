using GfxStudio.Core.Diagnostics;

namespace GfxStudio.Core.Output;

/// <summary>Outputs and diagnostics produced by a GfxEngine render operation.</summary>
public sealed class RenderJobResult(
    ulong masterSeed,
    IReadOnlyList<GeneratedArtifact> artifacts,
    IReadOnlyList<GfxDiagnostic> diagnostics,
    string? metadataFilePath = null) : IDisposable
{
    public ulong MasterSeed { get; } = masterSeed;

    public IReadOnlyList<GeneratedArtifact> Artifacts { get; } = artifacts;

    public IReadOnlyList<GfxDiagnostic> Diagnostics { get; } = diagnostics;

    public string? MetadataFilePath { get; } = metadataFilePath;

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    public void Dispose()
    {
        foreach (var artifact in Artifacts)
        {
            artifact.Dispose();
        }
    }
}
