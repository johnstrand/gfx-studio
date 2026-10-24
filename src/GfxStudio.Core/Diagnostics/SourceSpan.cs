namespace GfxStudio.Core.Diagnostics;

/// <summary>Identifies a range in a source file.</summary>
public readonly record struct SourceSpan(string FilePath, SourceLocation Start, SourceLocation End)
{
    public override string ToString() => $"{FilePath}({Start.Line},{Start.Column})";
}
