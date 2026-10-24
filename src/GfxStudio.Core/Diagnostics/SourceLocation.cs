namespace GfxStudio.Core.Diagnostics;

/// <summary>A one-based line and column with a zero-based source offset.</summary>
public readonly record struct SourceLocation(int Line, int Column, int Offset)
{
    public override string ToString() => $"{Line},{Column}";
}
