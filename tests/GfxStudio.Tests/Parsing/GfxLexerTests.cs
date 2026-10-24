using FluentAssertions;
using GfxStudio.Core.Parsing;

namespace GfxStudio.Tests.Parsing;

public sealed class GfxLexerTests
{
    [Fact]
    public void Tokenize_NumberGfx_HandlesInterpolatedFilename()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "number.gfx"));
        var lexer = new GfxLexer(source, "number.gfx");
        var tokens = lexer.Tokenize();

        lexer.Diagnostics.Should().BeEmpty();

        var interpolation = tokens
            .SkipWhile(token => token.Kind != GfxTokenKind.InterpolatedStringStart)
            .TakeWhile(token => token.Kind != GfxTokenKind.InterpolatedStringEnd)
            .Append(tokens.First(token => token.Kind == GfxTokenKind.InterpolatedStringEnd))
            .ToArray();

        interpolation.Select(token => token.Kind).Should().Equal(
            GfxTokenKind.InterpolatedStringStart,
            GfxTokenKind.InterpolatedStringText,
            GfxTokenKind.InterpolationStart,
            GfxTokenKind.Identifier,
            GfxTokenKind.Dot,
            GfxTokenKind.Identifier,
            GfxTokenKind.InterpolationFormat,
            GfxTokenKind.InterpolationEnd,
            GfxTokenKind.InterpolatedStringText,
            GfxTokenKind.InterpolatedStringEnd);
        interpolation.Select(token => token.Lexeme).Should().Equal(
            "\"", "worn_number_", "${", "run", ".", "number", ":0000", "}", ".png", "\"");
        interpolation[3].Span.Start.Should().Be(new GfxStudio.Core.Diagnostics.SourceLocation(23, 29, source.IndexOf("run.number", StringComparison.Ordinal)));
        interpolation[6].LiteralValue.Should().Be("0000");
    }

    [Fact]
    public void Tokenize_EmitsLiteralsKeywordsAndPunctuation()
    {
        var tokens = new GfxLexer("width = 64, ratio = .5\nflag = True\nother = False").Tokenize();

        tokens.Select(token => token.Kind).Should().Equal(
            GfxTokenKind.Identifier, GfxTokenKind.Equals, GfxTokenKind.NumberLiteral, GfxTokenKind.Comma,
            GfxTokenKind.Identifier, GfxTokenKind.Equals, GfxTokenKind.NumberLiteral, GfxTokenKind.Newline,
            GfxTokenKind.Identifier, GfxTokenKind.Equals, GfxTokenKind.True, GfxTokenKind.Newline,
            GfxTokenKind.Identifier, GfxTokenKind.Equals, GfxTokenKind.False, GfxTokenKind.EndOfFile);
        tokens[2].LiteralValue.Should().Be(64d);
        tokens[6].LiteralValue.Should().Be(0.5d);
    }

    [Fact]
    public void Tokenize_SkipsCommentsAndPreservesLineBreaks()
    {
        const string source = "a = 1 # line comment\nb = 2 // another\n/* block\ncomment */ c = 3";

        var lexer = new GfxLexer(source);
        var tokens = lexer.Tokenize();

        lexer.Diagnostics.Should().BeEmpty();
        tokens.Where(token => token.Kind == GfxTokenKind.Identifier).Select(token => token.Lexeme).Should().Equal("a", "b", "c");
        tokens.Count(token => token.Kind == GfxTokenKind.Newline).Should().Be(3);
        tokens.Single(token => token.Lexeme == "c").Span.Start.Line.Should().Be(4);
    }

    [Fact]
    public void Tokenize_RecognizesAllOperatorsAndDelimiters()
    {
        var tokens = new GfxLexer("{}()[],.:=+-*/%").Tokenize();

        tokens.Select(token => token.Kind).Should().Equal(
            GfxTokenKind.LeftBrace, GfxTokenKind.RightBrace, GfxTokenKind.LeftParen, GfxTokenKind.RightParen,
            GfxTokenKind.LeftBracket, GfxTokenKind.RightBracket, GfxTokenKind.Comma, GfxTokenKind.Dot,
            GfxTokenKind.Colon, GfxTokenKind.Equals, GfxTokenKind.Plus, GfxTokenKind.Minus,
            GfxTokenKind.Star, GfxTokenKind.Slash, GfxTokenKind.Percent, GfxTokenKind.EndOfFile);
    }

    [Fact]
    public void Tokenize_HandlesNestedBracesWithinInterpolation()
    {
        var lexer = new GfxLexer("\"${value({ nested: 1 }):0000}\"");
        var tokens = lexer.Tokenize();

        lexer.Diagnostics.Should().BeEmpty();
        tokens.Select(token => token.Kind).Should().ContainInOrder(
            GfxTokenKind.InterpolatedStringStart,
            GfxTokenKind.InterpolationStart,
            GfxTokenKind.LeftParen,
            GfxTokenKind.LeftBrace,
            GfxTokenKind.Colon,
            GfxTokenKind.RightBrace,
            GfxTokenKind.RightParen,
            GfxTokenKind.InterpolationFormat,
            GfxTokenKind.InterpolationEnd,
            GfxTokenKind.InterpolatedStringEnd);
    }

    [Fact]
    public void Tokenize_ReportsInvalidCharactersEscapesAndUnterminatedStrings()
    {
        var lexer = new GfxLexer("@ \"bad\\q\"\n\"unfinished", "bad.gfx");

        lexer.Tokenize();

        lexer.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal("LEX001", "LEX004", "LEX002");
        lexer.Diagnostics[0].Span.Start.Should().Be(new GfxStudio.Core.Diagnostics.SourceLocation(1, 1, 0));
        lexer.Diagnostics[1].Span.Start.Should().Be(new GfxStudio.Core.Diagnostics.SourceLocation(1, 7, 6));
        lexer.Diagnostics[2].Span.Start.Should().Be(new GfxStudio.Core.Diagnostics.SourceLocation(2, 1, 10));
    }

    [Fact]
    public void Tokenize_ReportsUnterminatedCommentsAndInterpolations()
    {
        var commentLexer = new GfxLexer("/* unfinished");
        commentLexer.Tokenize();
        commentLexer.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("LEX003");

        var interpolationLexer = new GfxLexer("\"${value");
        interpolationLexer.Tokenize();
        interpolationLexer.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("LEX006");
    }
}
