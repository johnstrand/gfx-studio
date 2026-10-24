using System.Globalization;
using System.Text;
using GfxStudio.Core.Diagnostics;

namespace GfxStudio.Core.Parsing;

/// <summary>Tokenizes .gfx source while retaining source locations and recoverable diagnostics.</summary>
public sealed class GfxLexer
{
    private readonly string _source;
    private readonly string _filePath;
    private readonly List<GfxToken> _tokens = [];
    private readonly List<GfxDiagnostic> _diagnostics = [];
    private int _offset;
    private int _line = 1;
    private int _column = 1;

    public GfxLexer(string source, string filePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filePath);
        _source = source;
        _filePath = filePath;
    }

    /// <summary>Diagnostics collected during the most recent call to <see cref="Tokenize"/>.</summary>
    public IReadOnlyList<GfxDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Returns all tokens, including a final end-of-file token.</summary>
    public IReadOnlyList<GfxToken> Tokenize()
    {
        _tokens.Clear();
        _diagnostics.Clear();
        _offset = 0;
        _line = 1;
        _column = 1;

        while (!IsAtEnd)
        {
            if (SkipWhitespaceAndComments())
            {
                continue;
            }

            ScanRegularToken();
        }

        var end = CurrentLocation;
        _tokens.Add(new GfxToken(GfxTokenKind.EndOfFile, string.Empty, null, Span(end, end)));
        return _tokens.AsReadOnly();
    }

    private bool IsAtEnd => _offset >= _source.Length;
    private SourceLocation CurrentLocation => new(_line, _column, _offset);
    private char Current => IsAtEnd ? '\0' : _source[_offset];
    private char Peek(int distance = 1) => _offset + distance < _source.Length ? _source[_offset + distance] : '\0';

    private bool SkipWhitespaceAndComments()
    {
        var startOffset = _offset;
        while (!IsAtEnd)
        {
            if (Current is ' ' or '\t' or '\f' or '\v')
            {
                Advance();
                continue;
            }

            if (Current is '\r' or '\n')
            {
                ScanNewline();
                continue;
            }

            if (Current == '#'
                || (Current == '/' && Peek() == '/'))
            {
                while (!IsAtEnd && Current is not '\r' and not '\n')
                {
                    Advance();
                }

                continue;
            }

            if (Current == '/' && Peek() == '*')
            {
                ScanBlockComment();
                continue;
            }

            break;
        }

        return _offset != startOffset;
    }

    private void ScanBlockComment()
    {
        var start = CurrentLocation;
        Advance();
        Advance();

        while (!IsAtEnd)
        {
            if (Current == '*' && Peek() == '/')
            {
                Advance();
                Advance();
                return;
            }

            if (Current is '\r' or '\n')
            {
                ScanNewline();
                continue;
            }

            Advance();
        }

        AddDiagnostic("LEX003", "Unterminated block comment.", start, CurrentLocation);
    }

    private void ScanRegularToken()
    {
        var start = CurrentLocation;
        var startOffset = _offset;

        if (IsIdentifierStart(Current))
        {
            ScanIdentifier(start, startOffset);
            return;
        }

        if (char.IsDigit(Current) || (Current == '.' && char.IsDigit(Peek())))
        {
            ScanNumber(start, startOffset);
            return;
        }

        switch (Current)
        {
            case '"':
                ScanString();
                return;
            case '{':
                ScanSingle(GfxTokenKind.LeftBrace, start, startOffset);
                return;
            case '}':
                ScanSingle(GfxTokenKind.RightBrace, start, startOffset);
                return;
            case '(':
                ScanSingle(GfxTokenKind.LeftParen, start, startOffset);
                return;
            case ')':
                ScanSingle(GfxTokenKind.RightParen, start, startOffset);
                return;
            case '[':
                ScanSingle(GfxTokenKind.LeftBracket, start, startOffset);
                return;
            case ']':
                ScanSingle(GfxTokenKind.RightBracket, start, startOffset);
                return;
            case ',':
                ScanSingle(GfxTokenKind.Comma, start, startOffset);
                return;
            case '.':
                ScanSingle(GfxTokenKind.Dot, start, startOffset);
                return;
            case '=':
                ScanSingle(GfxTokenKind.Equals, start, startOffset);
                return;
            case ':':
                ScanSingle(GfxTokenKind.Colon, start, startOffset);
                return;
            case '+':
                ScanSingle(GfxTokenKind.Plus, start, startOffset);
                return;
            case '-':
                ScanSingle(GfxTokenKind.Minus, start, startOffset);
                return;
            case '*':
                ScanSingle(GfxTokenKind.Star, start, startOffset);
                return;
            case '/':
                ScanSingle(GfxTokenKind.Slash, start, startOffset);
                return;
            case '%':
                ScanSingle(GfxTokenKind.Percent, start, startOffset);
                return;
            case '\r':
            case '\n':
                ScanNewline();
                return;
            default:
                Advance();
                AddDiagnostic("LEX001", $"Unexpected character '{_source[startOffset]}'.", start, CurrentLocation);
                return;
        }
    }

    private void ScanIdentifier(SourceLocation start, int startOffset)
    {
        Advance();
        while (IsIdentifierPart(Current))
        {
            Advance();
        }

        var lexeme = _source[startOffset.._offset];
        var (kind, value) = lexeme switch
        {
            "True" => (GfxTokenKind.True, (object?)true),
            "False" => (GfxTokenKind.False, (object?)false),
            _ => (GfxTokenKind.Identifier, null)
        };
        AddToken(kind, startOffset, start, value);
    }

    private void ScanNumber(SourceLocation start, int startOffset)
    {
        while (char.IsDigit(Current))
        {
            Advance();
        }

        if (Current == '.' && char.IsDigit(Peek()))
        {
            Advance();
            while (char.IsDigit(Current))
            {
                Advance();
            }
        }
        var lexeme = _source[startOffset.._offset];
        if (double.TryParse(lexeme, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            AddToken(GfxTokenKind.NumberLiteral, startOffset, start, value);
            return;
        }

        AddDiagnostic("LEX005", $"Invalid number literal '{lexeme}'.", start, CurrentLocation);
        AddToken(GfxTokenKind.NumberLiteral, startOffset, start, null);
    }

    private void ScanSingle(GfxTokenKind kind, SourceLocation start, int startOffset)
    {
        Advance();
        AddToken(kind, startOffset, start);
    }

    private void ScanString()
    {
        var stringStart = CurrentLocation;
        var stringStartOffset = _offset;
        Advance(); // opening quote

        var segmentStart = CurrentLocation;
        var segmentStartOffset = _offset;
        var decoded = new StringBuilder();
        var isInterpolated = false;

        while (!IsAtEnd)
        {
            if (Current == '"')
            {
                if (isInterpolated)
                {
                    EmitTextSegment(segmentStartOffset, segmentStart, decoded);
                    var endStart = CurrentLocation;
                    var endOffset = _offset;
                    Advance();
                    AddToken(GfxTokenKind.InterpolatedStringEnd, endOffset, endStart);
                }
                else
                {
                    Advance();
                    AddToken(GfxTokenKind.StringLiteral, stringStartOffset, stringStart, decoded.ToString());
                }

                return;
            }

            if (Current is '\r' or '\n')
            {
                AddDiagnostic("LEX002", "Unterminated string literal.", stringStart, CurrentLocation);
                if (isInterpolated)
                {
                    EmitTextSegment(segmentStartOffset, segmentStart, decoded);
                }
                else
                {
                    AddToken(GfxTokenKind.StringLiteral, stringStartOffset, stringStart, decoded.ToString());
                }

                return;
            }

            if (Current == '$' && Peek() == '{')
            {
                if (!isInterpolated)
                {
                    isInterpolated = true;
                    _tokens.Add(new GfxToken(
                        GfxTokenKind.InterpolatedStringStart,
                        _source[stringStartOffset..segmentStartOffset],
                        null,
                        Span(stringStart, segmentStart)));
                }

                EmitTextSegment(segmentStartOffset, segmentStart, decoded);
                var interpolationStart = CurrentLocation;
                var interpolationStartOffset = _offset;
                Advance();
                Advance();
                AddToken(GfxTokenKind.InterpolationStart, interpolationStartOffset, interpolationStart);
                ScanInterpolation(interpolationStart);

                segmentStart = CurrentLocation;
                segmentStartOffset = _offset;
                decoded.Clear();
                continue;
            }

            if (Current == '\\')
            {
                ScanEscape(decoded);
                continue;
            }

            decoded.Append(Current);
            Advance();
        }

        AddDiagnostic("LEX002", "Unterminated string literal.", stringStart, CurrentLocation);
        if (isInterpolated)
        {
            EmitTextSegment(segmentStartOffset, segmentStart, decoded);
        }
        else
        {
            AddToken(GfxTokenKind.StringLiteral, stringStartOffset, stringStart, decoded.ToString());
        }
    }

    private void ScanEscape(StringBuilder decoded)
    {
        var escapeStart = CurrentLocation;
        Advance(); // backslash
        if (IsAtEnd || Current is '\r' or '\n')
        {
            AddDiagnostic("LEX004", "Incomplete escape sequence.", escapeStart, CurrentLocation);
            return;
        }

        var escaped = Current;
        Advance();
        switch (escaped)
        {
            case '"': decoded.Append('"'); break;
            case '\\': decoded.Append('\\'); break;
            case 'n': decoded.Append('\n'); break;
            case 't': decoded.Append('\t'); break;
            default:
                AddDiagnostic("LEX004", $"Invalid escape sequence '\\{escaped}'.", escapeStart, CurrentLocation);
                decoded.Append(escaped);
                break;
        }
    }

    private void ScanInterpolation(SourceLocation interpolationStart)
    {
        var braceDepth = 0;
        while (!IsAtEnd)
        {
            if (SkipWhitespaceAndComments())
            {
                continue;
            }

            if (IsAtEnd)
            {
                break;
            }

            if (Current == '}' && braceDepth == 0)
            {
                var endStart = CurrentLocation;
                var endOffset = _offset;
                Advance();
                AddToken(GfxTokenKind.InterpolationEnd, endOffset, endStart);
                return;
            }

            if (Current == ':' && braceDepth == 0)
            {
                ScanInterpolationFormat();
                continue;
            }

            if (Current == '{')
            {
                var start = CurrentLocation;
                var startOffset = _offset;
                Advance();
                AddToken(GfxTokenKind.LeftBrace, startOffset, start);
                braceDepth++;
                continue;
            }

            if (Current == '}' && braceDepth > 0)
            {
                var start = CurrentLocation;
                var startOffset = _offset;
                Advance();
                AddToken(GfxTokenKind.RightBrace, startOffset, start);
                braceDepth--;
                continue;
            }

            ScanRegularToken();
        }

        AddDiagnostic("LEX006", "Unterminated string interpolation.", interpolationStart, CurrentLocation);
    }

    private void ScanInterpolationFormat()
    {
        var start = CurrentLocation;
        var startOffset = _offset;
        Advance(); // colon
        while (!IsAtEnd && Current is not '}' and not '\r' and not '\n')
        {
            Advance();
        }

        var lexeme = _source[startOffset.._offset];
        var value = lexeme.Length > 0 ? lexeme[1..] : string.Empty;
        AddToken(GfxTokenKind.InterpolationFormat, startOffset, start, value);
    }

    private void EmitTextSegment(int startOffset, SourceLocation start, StringBuilder decoded)
    {
        if (_offset == startOffset)
        {
            return;
        }

        AddToken(GfxTokenKind.InterpolatedStringText, startOffset, start, decoded.ToString());
    }

    private void ScanNewline()
    {
        var start = CurrentLocation;
        var startOffset = _offset;
        if (Current == '\r')
        {
            _offset++;
            if (!IsAtEnd && Current == '\n')
            {
                _offset++;
            }
        }
        else
        {
            _offset++;
        }

        _line++;
        _column = 1;
        AddToken(GfxTokenKind.Newline, startOffset, start);
    }

    private void Advance()
    {
        _offset++;
        _column++;
    }

    private void AddToken(GfxTokenKind kind, int startOffset, SourceLocation start, object? literalValue = null)
    {
        _tokens.Add(new GfxToken(kind, _source[startOffset.._offset], literalValue, Span(start, CurrentLocation)));
    }

    private void AddDiagnostic(string code, string message, SourceLocation start, SourceLocation end)
    {
        _diagnostics.Add(new GfxDiagnostic(code, message, Span(start, end), DiagnosticSeverity.Error));
    }

    private SourceSpan Span(SourceLocation start, SourceLocation end) => new(_filePath, start, end);

    private static bool IsIdentifierStart(char character) => character == '_' || char.IsLetter(character);
    private static bool IsIdentifierPart(char character) => character == '_' || char.IsLetterOrDigit(character);
}
