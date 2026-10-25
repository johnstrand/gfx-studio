using GfxStudio.Core.Diagnostics;

namespace GfxStudio.Editor;

/// <summary>
/// Aggregates the resolved .gfx output settings, the rendered preview frames, and diagnostics.
/// </summary>
public sealed class PreviewData
{
    public PreviewData(
        PreviewConfig? config,
        IReadOnlyList<PreviewFrame> frames,
        IReadOnlyList<GfxDiagnostic> diagnostics,
        string? error)
    {
        Config = config;
        Frames = frames;
        Diagnostics = diagnostics;
        Error = error;
    }

    public PreviewConfig? Config { get; }
    public IReadOnlyList<PreviewFrame> Frames { get; }
    public IReadOnlyList<GfxDiagnostic> Diagnostics { get; }
    public string? Error { get; }
}

/// <summary>One row in the editor's diagnostics list, with a source location for click-to-jump.</summary>
public sealed class DiagnosticRow
{
    public DiagnosticRow(DiagnosticSeverity severity, string code, string message, int line, int column, SourceSpan span)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Line = line;
        Column = column;
        Span = span;
    }

    public DiagnosticSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public int Line { get; }
    public int Column { get; }
    public SourceSpan Span { get; }
}