using GfxStudio.Core.Diagnostics;

namespace GfxStudio.Core.Output;

public sealed record OutputConfigBindResult(OutputConfig? Config, IReadOnlyList<GfxDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
