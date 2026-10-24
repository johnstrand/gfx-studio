namespace GfxStudio.Core.Parsing;

public enum GfxTokenKind
{
    NumberLiteral,
    StringLiteral,
    InterpolatedStringStart,
    InterpolatedStringText,
    InterpolationStart,
    InterpolationFormat,
    InterpolationEnd,
    InterpolatedStringEnd,
    Identifier,
    True,
    False,
    LeftBrace,
    RightBrace,
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    Dot,
    Equals,
    Colon,
    Newline,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    EndOfFile
}
