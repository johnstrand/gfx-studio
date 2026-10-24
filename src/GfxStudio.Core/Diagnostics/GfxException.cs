namespace GfxStudio.Core.Diagnostics;

/// <summary>An exception containing one or more structured .gfx diagnostics.</summary>
public sealed class GfxException : Exception
{
    public GfxException(IReadOnlyList<GfxDiagnostic> Diagnostics)
        : base(FormatMessage(Diagnostics))
    {
        ArgumentNullException.ThrowIfNull(Diagnostics);
        this.Diagnostics = Array.AsReadOnly(Diagnostics.ToArray());
    }

    public GfxException(GfxDiagnostic Diagnostic)
        : this(new[] { Diagnostic })
    {
    }

    public IReadOnlyList<GfxDiagnostic> Diagnostics { get; }

    private static string FormatMessage(IReadOnlyList<GfxDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Count == 0
            ? "A .gfx error occurred."
            : string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
    }
}
