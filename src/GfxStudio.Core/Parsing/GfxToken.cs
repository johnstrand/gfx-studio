using GfxStudio.Core.Diagnostics;

namespace GfxStudio.Core.Parsing;

/// <summary>A token produced by the .gfx lexer.</summary>
public readonly record struct GfxToken(
    GfxTokenKind Kind,
    string Lexeme,
    object? LiteralValue,
    SourceSpan Span);
