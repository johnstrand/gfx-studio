using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Parsing.Ast;

namespace GfxStudio.Core.Parsing;

/// <summary>Represents the result of parsing a .gfx document, including any syntax diagnostics.</summary>
public sealed record ParseResult(
    GfxDocument? Document,
    IReadOnlyList<GfxDiagnostic> Diagnostics)
{
    /// <summary>Gets a value indicating whether any error diagnostics were reported.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
