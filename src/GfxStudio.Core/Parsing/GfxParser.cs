using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Parsing.Ast;

namespace GfxStudio.Core.Parsing;

/// <summary>
/// Recursive-descent and Pratt expression parser for .gfx definition files.
/// </summary>
public sealed class GfxParser
{
    private const int PrecedenceNone = 0;
    private const int PrecedenceAdditive = 10;
    private const int PrecedenceMultiplicative = 20;
    private const int PrecedenceUnary = 30;
    private const int PrecedencePostfix = 40;

    private readonly IReadOnlyList<GfxToken> _tokens;
    private readonly List<GfxDiagnostic> _diagnostics = [];
    private int _index;

    public GfxParser(IReadOnlyList<GfxToken> tokens, IEnumerable<GfxDiagnostic>? initialDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0 || tokens[^1].Kind != GfxTokenKind.EndOfFile)
        {
            throw new ArgumentException("Token stream must end with an EndOfFile token.", nameof(tokens));
        }

        _tokens = tokens;
        if (initialDiagnostics is not null)
        {
            _diagnostics.AddRange(initialDiagnostics);
        }
    }

    /// <summary>Lexes and parses the given .gfx source text into an AST document and diagnostics.</summary>
    public static ParseResult Parse(string source, string filePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filePath);

        var lexer = new GfxLexer(source, filePath);
        var tokens = lexer.Tokenize();
        var parser = new GfxParser(tokens, lexer.Diagnostics);
        return parser.ParseDocument();
    }

    /// <summary>Parses the token stream into a <see cref="ParseResult"/>.</summary>
    public ParseResult ParseDocument()
    {
        var startLocation = Current.Span.Start;
        var properties = new List<PropertyAssignmentNode>();

        SkipNewlines();

        while (!IsAtEnd)
        {
            if (Current.Kind == GfxTokenKind.RightBrace)
            {
                var strayBrace = Advance();
                AddDiagnostic("PAR001", "Unexpected closing brace '}' at top level.", strayBrace.Span);
                SkipStatementSeparators();
                continue;
            }

            var assignment = ParsePropertyAssignment(isInsideBlock: false);
            if (assignment is not null)
            {
                properties.Add(assignment);
            }

            SkipStatementSeparators();
        }

        var endLocation = Current.Span.End;
        var documentSpan = new SourceSpan(Current.Span.FilePath, startLocation, endLocation);
        var document = new GfxDocument(properties.AsReadOnly(), documentSpan);

        return new ParseResult(document, _diagnostics.AsReadOnly());
    }

    private GfxToken Current => _tokens[_index];

    private GfxToken Previous => _tokens[_index - 1];

    private bool IsAtEnd => Current.Kind == GfxTokenKind.EndOfFile;

    private PropertyAssignmentNode? ParsePropertyAssignment(bool isInsideBlock)
    {
        SkipNewlines();
        if (IsAtEnd || (isInsideBlock && Current.Kind == GfxTokenKind.RightBrace))
        {
            return null;
        }

        if (Current.Kind != GfxTokenKind.Identifier)
        {
            var unexpected = Current;
            AddDiagnostic("PAR002", $"Expected property name identifier, but found '{DisplayToken(unexpected)}'.", unexpected.Span);
            SynchronizeStatement(isInsideBlock);
            return null;
        }

        var nameToken = Advance();
        SkipNewlines();

        if (Current.Kind is not GfxTokenKind.Equals and not GfxTokenKind.Colon)
        {
            var unexpected = Current;
            AddDiagnostic(
                "PAR003",
                $"Expected '=' or ':' after property name '{nameToken.Lexeme}', but found '{DisplayToken(unexpected)}'.",
                unexpected.Span);
            SynchronizeStatement(isInsideBlock);
            return null;
        }

        Advance(); // consume '=' or ':'
        SkipNewlines();

        var valueExpr = ParseExpression(PrecedenceNone);
        if (valueExpr is null)
        {
            SynchronizeStatement(isInsideBlock);
            return null;
        }

        // Check if another token immediately follows on the same line without a valid separator
        if (!IsAtEnd
            && Previous.Kind is not GfxTokenKind.Newline and not GfxTokenKind.Comma
            && Current.Kind is not GfxTokenKind.Newline
                and not GfxTokenKind.Comma
                and not GfxTokenKind.RightBrace)
        {
            var unexpected = Current;
            AddDiagnostic(
                "PAR004",
                $"Expected ',' or newline after property assignment '{nameToken.Lexeme}', but found '{DisplayToken(unexpected)}'.",
                unexpected.Span);
            SynchronizeStatement(isInsideBlock);
        }

        var span = CombineSpans(nameToken.Span, valueExpr.Span);
        return new PropertyAssignmentNode(nameToken.Lexeme, valueExpr, span);
    }

    private ObjectBlockExpr ParseObjectBlock()
    {
        var openBrace = Advance(); // consume '{'
        var properties = new List<PropertyAssignmentNode>();

        SkipStatementSeparators();

        while (!IsAtEnd && Current.Kind != GfxTokenKind.RightBrace)
        {
            var assignment = ParsePropertyAssignment(isInsideBlock: true);
            if (assignment is not null)
            {
                properties.Add(assignment);
            }

            SkipStatementSeparators();
        }

        if (Current.Kind == GfxTokenKind.RightBrace)
        {
            var closeBrace = Advance();
            return new ObjectBlockExpr(properties.AsReadOnly(), CombineSpans(openBrace.Span, closeBrace.Span));
        }

        AddDiagnostic("PAR005", "Expected closing '}' for object block.", openBrace.Span);
        var fallbackEnd = properties.Count > 0 ? properties[^1].Span : openBrace.Span;
        return new ObjectBlockExpr(properties.AsReadOnly(), CombineSpans(openBrace.Span, fallbackEnd));
    }

    private ArrayExpr ParseArrayLiteral()
    {
        var openBracket = Advance(); // consume '['
        var elements = new List<GfxExpr>();

        SkipNewlines();

        while (!IsAtEnd && Current.Kind != GfxTokenKind.RightBracket)
        {
            if (Current.Kind == GfxTokenKind.RightBrace || IsPropertyAssignmentStart())
            {
                break;
            }

            var element = ParseExpression(PrecedenceNone);
            if (element is not null)
            {
                elements.Add(element);
            }
            else
            {
                SynchronizeDelimiter(GfxTokenKind.Comma, GfxTokenKind.RightBracket);
            }

            SkipNewlines();

            if (Current.Kind == GfxTokenKind.Comma)
            {
                Advance();
                SkipNewlines();
                continue;
            }

            if (Current.Kind is GfxTokenKind.RightBracket or GfxTokenKind.RightBrace || IsPropertyAssignmentStart())
            {
                break;
            }

            if (!IsAtEnd)
            {
                var unexpected = Current;
                AddDiagnostic("PAR006", $"Expected ',' or ']' in array literal, but found '{DisplayToken(unexpected)}'.", unexpected.Span);
                SynchronizeDelimiter(GfxTokenKind.Comma, GfxTokenKind.RightBracket);
                if (Current.Kind == GfxTokenKind.Comma)
                {
                    Advance();
                    SkipNewlines();
                }
            }
        }

        if (Current.Kind == GfxTokenKind.RightBracket)
        {
            var closeBracket = Advance();
            return new ArrayExpr(elements.AsReadOnly(), CombineSpans(openBracket.Span, closeBracket.Span));
        }

        AddDiagnostic("PAR007", "Expected closing ']' for array literal.", openBracket.Span);
        var fallbackEnd = elements.Count > 0 ? elements[^1].Span : openBracket.Span;
        return new ArrayExpr(elements.AsReadOnly(), CombineSpans(openBracket.Span, fallbackEnd));
    }

    private GfxExpr? ParseExpression(int minPrecedence)
    {
        var left = ParsePrefixExpression();
        if (left is null)
        {
            return null;
        }

        while (!IsAtEnd)
        {
            // Postfix operators: '.' (MemberAccess) and '(' (FunctionCall)
            if (Current.Kind == GfxTokenKind.Dot && PrecedencePostfix > minPrecedence)
            {
                var dotToken = Advance();
                SkipNewlines();
                if (Current.Kind != GfxTokenKind.Identifier)
                {
                    AddDiagnostic("PAR008", $"Expected member name identifier after '.', but found '{DisplayToken(Current)}'.", Current.Span);
                    return left;
                }

                var memberToken = Advance();
                left = new MemberAccessExpr(left, memberToken.Lexeme, CombineSpans(left.Span, memberToken.Span));
                continue;
            }

            if (Current.Kind == GfxTokenKind.LeftParen && PrecedencePostfix > minPrecedence)
            {
                left = ParseFunctionCall(left);
                continue;
            }

            // Binary infix operators: '+', '-', '*', '/', '%'
            var binaryOp = TryGetBinaryOperator(Current.Kind);
            if (binaryOp is null)
            {
                break;
            }

            var precedence = GetBinaryPrecedence(binaryOp.Value);
            if (precedence <= minPrecedence)
            {
                break;
            }

            var opToken = Advance();
            SkipNewlines();

            var right = ParseExpression(precedence);
            if (right is null)
            {
                return left;
            }

            left = new BinaryExpr(left, binaryOp.Value, right, CombineSpans(left.Span, right.Span));
        }

        return left;
    }

    private GfxExpr? ParsePrefixExpression()
    {
        switch (Current.Kind)
        {
            case GfxTokenKind.Plus:
            {
                var opToken = Advance();
                SkipNewlines();
                var operand = ParseExpression(PrecedenceUnary);
                if (operand is null)
                {
                    return null;
                }

                return new UnaryExpr(UnaryOperator.Plus, operand, CombineSpans(opToken.Span, operand.Span));
            }

            case GfxTokenKind.Minus:
            {
                var opToken = Advance();
                SkipNewlines();
                var operand = ParseExpression(PrecedenceUnary);
                if (operand is null)
                {
                    return null;
                }

                return new UnaryExpr(UnaryOperator.Minus, operand, CombineSpans(opToken.Span, operand.Span));
            }

            case GfxTokenKind.NumberLiteral:
            {
                var token = Advance();
                var value = token.LiteralValue is double d ? d : 0d;
                var isInteger = !token.Lexeme.Contains('.');
                return new NumberLiteralExpr(value, isInteger, token.Span);
            }

            case GfxTokenKind.StringLiteral:
            {
                var token = Advance();
                var value = token.LiteralValue as string ?? string.Empty;
                return new StringLiteralExpr(value, token.Span);
            }

            case GfxTokenKind.InterpolatedStringStart:
                return ParseInterpolatedString();

            case GfxTokenKind.True:
            {
                var token = Advance();
                return new BooleanLiteralExpr(true, token.Span);
            }

            case GfxTokenKind.False:
            {
                var token = Advance();
                return new BooleanLiteralExpr(false, token.Span);
            }

            case GfxTokenKind.Identifier:
            {
                var token = Advance();
                if (token.Lexeme == "true")
                {
                    return new BooleanLiteralExpr(true, token.Span);
                }

                if (token.Lexeme == "false")
                {
                    return new BooleanLiteralExpr(false, token.Span);
                }

                return new IdentifierExpr(token.Lexeme, token.Span);
            }

            case GfxTokenKind.LeftBrace:
                return ParseObjectBlock();

            case GfxTokenKind.LeftBracket:
                return ParseArrayLiteral();

            case GfxTokenKind.LeftParen:
                return ParseParenthesizedExpression();

            default:
            {
                var unexpected = Current;
                AddDiagnostic("PAR009", $"Expected expression, but found '{DisplayToken(unexpected)}'.", unexpected.Span);
                return null;
            }
        }
    }

    private GfxExpr ParseFunctionCall(GfxExpr callee)
    {
        var openParen = Advance(); // consume '('
        var arguments = new List<GfxExpr>();

        SkipNewlines();

        while (!IsAtEnd && Current.Kind != GfxTokenKind.RightParen)
        {
            if (Current.Kind == GfxTokenKind.RightBrace || IsPropertyAssignmentStart())
            {
                break;
            }

            var arg = ParseExpression(PrecedenceNone);
            if (arg is not null)
            {
                arguments.Add(arg);
            }
            else
            {
                SynchronizeDelimiter(GfxTokenKind.Comma, GfxTokenKind.RightParen);
            }

            SkipNewlines();

            if (Current.Kind == GfxTokenKind.Comma)
            {
                Advance();
                SkipNewlines();
                continue;
            }

            if (Current.Kind is GfxTokenKind.RightParen or GfxTokenKind.RightBrace || IsPropertyAssignmentStart())
            {
                break;
            }

            if (!IsAtEnd)
            {
                var unexpected = Current;
                AddDiagnostic("PAR010", $"Expected ',' or ')' in function call arguments, but found '{DisplayToken(unexpected)}'.", unexpected.Span);
                SynchronizeDelimiter(GfxTokenKind.Comma, GfxTokenKind.RightParen);
                if (Current.Kind == GfxTokenKind.Comma)
                {
                    Advance();
                    SkipNewlines();
                }
            }
        }

        if (Current.Kind == GfxTokenKind.RightParen)
        {
            var closeParen = Advance();
            return new FunctionCallExpr(callee, arguments.AsReadOnly(), CombineSpans(callee.Span, closeParen.Span));
        }

        AddDiagnostic("PAR011", "Expected closing ')' for function call.", openParen.Span);
        var fallbackEnd = arguments.Count > 0 ? arguments[^1].Span : openParen.Span;
        return new FunctionCallExpr(callee, arguments.AsReadOnly(), CombineSpans(callee.Span, fallbackEnd));
    }

    private GfxExpr? ParseParenthesizedExpression()
    {
        var openParen = Advance(); // consume '('
        SkipNewlines();

        var inner = ParseExpression(PrecedenceNone);
        SkipNewlines();

        if (Current.Kind == GfxTokenKind.RightParen)
        {
            var closeParen = Advance();
            return inner;
        }

        AddDiagnostic("PAR012", "Expected closing ')' after parenthesized expression.", openParen.Span);
        SynchronizeDelimiter(GfxTokenKind.RightParen, GfxTokenKind.Newline);
        if (Current.Kind == GfxTokenKind.RightParen)
        {
            Advance();
        }

        return inner;
    }

    private InterpolatedStringExpr ParseInterpolatedString()
    {
        var startToken = Advance(); // consume InterpolatedStringStart
        var parts = new List<InterpolatedPart>();

        while (!IsAtEnd && Current.Kind != GfxTokenKind.InterpolatedStringEnd)
        {
            if (Current.Kind == GfxTokenKind.InterpolatedStringText)
            {
                var textToken = Advance();
                var text = textToken.LiteralValue as string ?? string.Empty;
                parts.Add(new InterpolatedTextPart(text, textToken.Span));
                continue;
            }

            if (Current.Kind == GfxTokenKind.InterpolationStart)
            {
                var interpStart = Advance(); // consume '${'
                SkipNewlines();

                var expr = ParseExpression(PrecedenceNone);
                SkipNewlines();

                string? format = null;
                if (Current.Kind == GfxTokenKind.InterpolationFormat)
                {
                    var formatToken = Advance();
                    format = formatToken.LiteralValue as string ?? string.Empty;
                }

                SourceSpan interpEndSpan;
                if (Current.Kind == GfxTokenKind.InterpolationEnd)
                {
                    interpEndSpan = Advance().Span;
                }
                else
                {
                    AddDiagnostic("PAR013", "Expected closing '}' for string interpolation.", interpStart.Span);
                    SynchronizeDelimiter(GfxTokenKind.InterpolationEnd, GfxTokenKind.InterpolatedStringEnd);
                    interpEndSpan = Current.Kind == GfxTokenKind.InterpolationEnd ? Advance().Span : Previous.Span;
                }

                if (expr is not null)
                {
                    parts.Add(new InterpolatedExpressionPart(expr, format, CombineSpans(interpStart.Span, interpEndSpan)));
                }

                continue;
            }

            // Unexpected token inside interpolated string (e.g., if string was unterminated at newline)
            break;
        }

        if (Current.Kind == GfxTokenKind.InterpolatedStringEnd)
        {
            var endToken = Advance();
            return new InterpolatedStringExpr(parts.AsReadOnly(), CombineSpans(startToken.Span, endToken.Span));
        }

        var fallbackSpan = parts.Count > 0 ? CombineSpans(startToken.Span, parts[^1].Span) : startToken.Span;
        return new InterpolatedStringExpr(parts.AsReadOnly(), fallbackSpan);
    }

    private static BinaryOperator? TryGetBinaryOperator(GfxTokenKind kind) => kind switch
    {
        GfxTokenKind.Plus => BinaryOperator.Add,
        GfxTokenKind.Minus => BinaryOperator.Subtract,
        GfxTokenKind.Star => BinaryOperator.Multiply,
        GfxTokenKind.Slash => BinaryOperator.Divide,
        GfxTokenKind.Percent => BinaryOperator.Modulo,
        _ => null
    };

    private static int GetBinaryPrecedence(BinaryOperator op) => op switch
    {
        BinaryOperator.Add or BinaryOperator.Subtract => PrecedenceAdditive,
        BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo => PrecedenceMultiplicative,
        _ => PrecedenceNone
    };

    private void SkipNewlines()
    {
        while (!IsAtEnd && Current.Kind == GfxTokenKind.Newline)
        {
            Advance();
        }
    }

    private void SkipStatementSeparators()
    {
        while (!IsAtEnd && Current.Kind is GfxTokenKind.Newline or GfxTokenKind.Comma)
        {
            Advance();
        }
    }

    private void SynchronizeStatement(bool isInsideBlock)
    {
        var braceDepth = 0;
        while (!IsAtEnd)
        {
            if (Current.Kind == GfxTokenKind.LeftBrace)
            {
                braceDepth++;
                Advance();
                continue;
            }

            if (Current.Kind == GfxTokenKind.RightBrace)
            {
                if (braceDepth > 0)
                {
                    braceDepth--;
                    Advance();
                    if (braceDepth == 0)
                    {
                        return;
                    }

                    continue;
                }

                if (isInsideBlock)
                {
                    return; // let enclosing block consume its closing '}'
                }

                Advance();
                return;
            }

            if (braceDepth == 0 && Current.Kind is GfxTokenKind.Newline or GfxTokenKind.Comma)
            {
                Advance();
                return;
            }

            Advance();
        }
    }

    private void SynchronizeDelimiter(GfxTokenKind delimiter1, GfxTokenKind delimiter2)
    {
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        while (!IsAtEnd)
        {
            if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0
                && (Current.Kind == delimiter1
                    || Current.Kind == delimiter2
                    || Current.Kind == GfxTokenKind.RightBrace
                    || IsPropertyAssignmentStart()))
            {
                return;
            }

            switch (Current.Kind)
            {
                case GfxTokenKind.LeftParen:
                    parenDepth++;
                    break;
                case GfxTokenKind.RightParen when parenDepth > 0:
                    parenDepth--;
                    break;
                case GfxTokenKind.LeftBracket:
                    bracketDepth++;
                    break;
                case GfxTokenKind.RightBracket when bracketDepth > 0:
                    bracketDepth--;
                    break;
                case GfxTokenKind.LeftBrace:
                    braceDepth++;
                    break;
                case GfxTokenKind.RightBrace when braceDepth > 0:
                    braceDepth--;
                    break;
            }

            Advance();
        }
    }

    private bool IsPropertyAssignmentStart()
    {
        if (Current.Kind != GfxTokenKind.Identifier)
        {
            return false;
        }

        var lookahead = _index + 1;
        while (lookahead < _tokens.Count && _tokens[lookahead].Kind == GfxTokenKind.Newline)
        {
            lookahead++;
        }

        return lookahead < _tokens.Count
            && _tokens[lookahead].Kind is GfxTokenKind.Equals or GfxTokenKind.Colon;
    }

    private GfxToken Advance()
    {
        if (!IsAtEnd)
        {
            _index++;
        }

        return _tokens[_index - 1];
    }

    private void AddDiagnostic(string code, string message, SourceSpan span)
    {
        _diagnostics.Add(new GfxDiagnostic(code, message, span, DiagnosticSeverity.Error));
    }

    private static SourceSpan CombineSpans(SourceSpan start, SourceSpan end) =>
        new(start.FilePath, start.Start, end.End);

    private static string DisplayToken(GfxToken token) => token.Kind switch
    {
        GfxTokenKind.EndOfFile => "<eof>",
        GfxTokenKind.Newline => "<newline>",
        _ => token.Lexeme
    };
}
