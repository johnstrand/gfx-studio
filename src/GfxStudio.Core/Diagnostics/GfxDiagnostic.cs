namespace GfxStudio.Core.Diagnostics;

public sealed record GfxDiagnostic(
    string Code,
    string Message,
    SourceSpan Span,
    DiagnosticSeverity Severity)
{
    public override string ToString() => $"{Span}: {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}
