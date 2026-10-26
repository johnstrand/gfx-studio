using System.Globalization;
using System.Text;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Parsing.Ast;

namespace GfxStudio.Core.Formatting;

/// <summary>Result of formatting a <c>.gfx</c> document with <see cref="GfxFormatter"/>.</summary>
public sealed record GfxFormatResult(
    string FormattedText,
    bool Changed,
    IReadOnlyList<GfxDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Comment-preserving, idempotent source code formatter for <c>.gfx</c> documents.
/// </summary>
public static class GfxFormatter
{
    private static readonly HashSet<string> CompactLeafPropertyNames = new(StringComparer.Ordinal)
    {
        "dimensions", "size", "offset", "position", "anchor", "pivot",
        "crop", "scale", "transform_scale", "shear", "pattern_offset",
        "phase_offset", "align", "center", "start", "end", "p1", "p2", "p3",
        "cell_dimensions"
    };

    private static readonly HashSet<string> CompactLeafKeyNames = new(StringComparer.Ordinal)
    {
        "width", "height", "x", "y", "r", "g", "b", "a",
        "pos", "position", "color", "horizontal", "vertical",
        "u", "v", "min", "max", "columns", "rows"
    };

    private static readonly HashSet<string> StructuralBlockPropertyNames = new(StringComparer.Ordinal)
    {
        "variables", "scripts", "styles", "layers", "output", "transform"
    };

    /// <summary>
    /// Formats <paramref name="source"/> into canonical 2-space indented <c>.gfx</c> syntax.
    /// If the document contains syntax errors, returns <paramref name="source"/> unchanged along with parse diagnostics.
    /// </summary>
    public static GfxFormatResult Format(string source, string filePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filePath);

        var parseResult = GfxParser.Parse(source, filePath);
        if (parseResult.HasErrors || parseResult.Document is null)
        {
            return new GfxFormatResult(source, false, parseResult.Diagnostics);
        }

        var comments = ScanComments(source);
        var blankLines = ScanBlankLineSet(source);
        var state = new FormatterState(source, comments, blankLines);
        var formatted = state.FormatDocument(parseResult.Document);

        var changed = !string.Equals(NormalizeNewlines(source), formatted, StringComparison.Ordinal);
        return new GfxFormatResult(formatted, changed, parseResult.Diagnostics);
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private sealed record CommentTrivia(
        string Text,
        int StartOffset,
        int EndOffset,
        int StartLine,
        int EndLine,
        bool IsTrailing);

    private static List<CommentTrivia> ScanComments(string source)
    {
        var result = new List<CommentTrivia>();
        var i = 0;
        var line = 1;
        var hasCodeOnCurrentLine = false;

        while (i < source.Length)
        {
            var c = source[i];
            if (c == '\r')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n')
                {
                    i += 2;
                }
                else
                {
                    i++;
                }

                line++;
                hasCodeOnCurrentLine = false;
                continue;
            }

            if (c == '\n')
            {
                i++;
                line++;
                hasCodeOnCurrentLine = false;
                continue;
            }

            if (c is ' ' or '\t' or '\f' or '\v')
            {
                i++;
                continue;
            }

            // String literal: skip contents so '#' or '//' inside strings are not treated as comments
            if (c == '"')
            {
                hasCodeOnCurrentLine = true;
                i++;
                while (i < source.Length && source[i] != '"')
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (source[i] == '\r')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '\n')
                        {
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }

                        line++;
                        continue;
                    }

                    if (source[i] == '\n')
                    {
                        i++;
                        line++;
                        continue;
                    }

                    i++;
                }

                if (i < source.Length && source[i] == '"')
                {
                    i++;
                }

                continue;
            }

            // Line comment: '#' or '//'
            if (c == '#' || (c == '/' && i + 1 < source.Length && source[i + 1] == '/'))
            {
                var startOffset = i;
                var startLine = line;
                var isTrailing = hasCodeOnCurrentLine;
                while (i < source.Length && source[i] is not '\r' and not '\n')
                {
                    i++;
                }

                var raw = source[startOffset..i].TrimEnd();
                result.Add(new CommentTrivia(raw, startOffset, i, startLine, startLine, isTrailing));
                continue;
            }

            // Block comment: '/* ... */'
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var startOffset = i;
                var startLine = line;
                var isTrailing = hasCodeOnCurrentLine;
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }

                    if (source[i] == '\r')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '\n')
                        {
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }

                        line++;
                        continue;
                    }

                    if (source[i] == '\n')
                    {
                        i++;
                        line++;
                        continue;
                    }

                    i++;
                }

                var endLine = line;
                var raw = NormalizeNewlines(source[startOffset..i]).Trim();
                result.Add(new CommentTrivia(raw, startOffset, i, startLine, endLine, isTrailing));
                continue;
            }

            hasCodeOnCurrentLine = true;
            i++;
        }

        return result;
    }

    private static HashSet<int> ScanBlankLineSet(string source)
    {
        var blankLines = new HashSet<int>();
        var normalized = NormalizeNewlines(source);
        var lines = normalized.Split('\n');
        for (var idx = 0; idx < lines.Length; idx++)
        {
            if (string.IsNullOrWhiteSpace(lines[idx]))
            {
                blankLines.Add(idx + 1); // 1-based line number
            }
        }

        return blankLines;
    }

    private sealed class FormatterState
    {
        private readonly string _source;
        private readonly List<CommentTrivia> _comments;
        private readonly HashSet<int> _blankLines;
        private readonly bool[] _commentEmitted;
        private readonly StringBuilder _sb = new();

        public FormatterState(string source, List<CommentTrivia> comments, HashSet<int> blankLines)
        {
            _source = source;
            _comments = comments;
            _blankLines = blankLines;
            _commentEmitted = new bool[comments.Count];
        }

        public string FormatDocument(GfxDocument document)
        {
            var lastEndLine = 0;
            for (var i = 0; i < document.Properties.Count; i++)
            {
                var prop = document.Properties[i];
                EmitLeadingCommentsBefore(prop.Span.Start.Offset, indentLevel: 0, ref lastEndLine);

                if (i > 0 || lastEndLine > 0)
                {
                    var isTopLevelSectionBreak = i > 0;
                    var hadBlankLine = HasBlankLineBetween(lastEndLine, prop.Span.Start.Line);
                    if ((isTopLevelSectionBreak || hadBlankLine) && !EndsWithBlankLine())
                    {
                        EnsureNewline();
                        _sb.Append('\n');
                    }
                    else
                    {
                        EnsureNewline();
                    }
                }

                FormatPropertyAssignment(prop, indentLevel: 0, parentBlockName: null);
                lastEndLine = GetActualEndLine(prop.Span);
                EmitTrailingCommentOnLine(lastEndLine);
            }

            EmitRemainingComments(indentLevel: 0, ref lastEndLine);

            var output = _sb.ToString().TrimEnd();
            return output.Length == 0 ? string.Empty : output + "\n";
        }

        private void FormatPropertyAssignment(PropertyAssignmentNode prop, int indentLevel, string? parentBlockName)
        {
            AppendIndent(indentLevel);
            _sb.Append(prop.Name);
            _sb.Append(" = ");
            FormatExpression(prop.Value, indentLevel, prop.Name, parentBlockName);
        }

        private void FormatExpression(GfxExpr expr, int indentLevel, string? propertyName, string? parentBlockName)
        {
            switch (expr)
            {
                case ObjectBlockExpr objectBlock:
                    FormatObjectBlock(objectBlock, indentLevel, propertyName, parentBlockName);
                    break;

                case ArrayExpr arrayExpr:
                    FormatArray(arrayExpr, indentLevel, propertyName);
                    break;

                case NumberLiteralExpr numberLiteral:
                    _sb.Append(FormatNumberLiteral(numberLiteral));
                    break;

                case StringLiteralExpr stringLiteral:
                    _sb.Append(FormatStringLiteral(stringLiteral.Value));
                    break;

                case BooleanLiteralExpr boolLiteral:
                    _sb.Append(boolLiteral.Value ? "true" : "false");
                    break;

                case InterpolatedStringExpr interpolatedString:
                    FormatInterpolatedString(interpolatedString, indentLevel);
                    break;

                case IdentifierExpr identifierExpr:
                    _sb.Append(identifierExpr.Name);
                    break;

                case MemberAccessExpr memberAccess:
                    FormatExpression(memberAccess.Target, indentLevel, null, null);
                    _sb.Append('.');
                    _sb.Append(memberAccess.MemberName);
                    break;

                case FunctionCallExpr functionCall:
                    FormatFunctionCall(functionCall, indentLevel);
                    break;

                case UnaryExpr unaryExpr:
                    FormatUnary(unaryExpr, indentLevel);
                    break;

                case BinaryExpr binaryExpr:
                    FormatBinary(binaryExpr, indentLevel);
                    break;
            }
        }

        private void FormatObjectBlock(
            ObjectBlockExpr block,
            int indentLevel,
            string? propertyName,
            string? parentBlockName)
        {
            if (block.Properties.Count == 0 && !HasUnemittedCommentsInSpan(block.Span))
            {
                _sb.Append("{}");
                return;
            }

            if (ShouldFormatObjectInline(block, indentLevel, propertyName, parentBlockName))
            {
                _sb.Append("{ ");
                for (var i = 0; i < block.Properties.Count; i++)
                {
                    if (i > 0)
                    {
                        _sb.Append(", ");
                    }

                    var p = block.Properties[i];
                    _sb.Append(p.Name);
                    _sb.Append(" = ");
                    FormatExpression(p.Value, indentLevel, p.Name, propertyName);
                }

                _sb.Append(" }");
                return;
            }

            _sb.Append("{\n");
            var childIndent = indentLevel + 1;
            var lastEndLine = block.Span.Start.Line;

            for (var i = 0; i < block.Properties.Count; i++)
            {
                var prop = block.Properties[i];
                EmitLeadingCommentsBefore(prop.Span.Start.Offset, childIndent, ref lastEndLine, maxOffset: block.Span.End.Offset);

                if (i > 0 && HasBlankLineBetween(lastEndLine, prop.Span.Start.Line) && !EndsWithBlankLine())
                {
                    EnsureNewline();
                    _sb.Append('\n');
                }
                else
                {
                    EnsureNewline();
                }

                FormatPropertyAssignment(prop, childIndent, propertyName);
                lastEndLine = GetActualEndLine(prop.Span);
                EmitTrailingCommentOnLine(lastEndLine);
            }

            EmitLeadingCommentsBefore(block.Span.End.Offset, childIndent, ref lastEndLine, maxOffset: block.Span.End.Offset);
            EnsureNewline();
            AppendIndent(indentLevel);
            _sb.Append('}');
        }

        private bool ShouldFormatObjectInline(
            ObjectBlockExpr block,
            int indentLevel,
            string? propertyName,
            string? parentBlockName)
        {
            if (indentLevel == 0)
            {
                return false;
            }

            if (propertyName is not null && StructuralBlockPropertyNames.Contains(propertyName))
            {
                return false;
            }

            if (string.Equals(parentBlockName, "layers", StringComparison.Ordinal) ||
                string.Equals(parentBlockName, "styles", StringComparison.Ordinal))
            {
                return false;
            }

            if (HasUnemittedCommentsInSpan(block.Span))
            {
                return false;
            }

            if (block.Properties.Count is 0 or > 6)
            {
                return false;
            }

            // Never inline an object that contains nested ObjectBlockExpr or ArrayExpr
            foreach (var p in block.Properties)
            {
                if (p.Value is ObjectBlockExpr or ArrayExpr)
                {
                    return false;
                }
            }

            // 1. Known compact leaf property names (e.g., dimensions, size, offset, anchor, crop, align, pattern_offset)
            if (propertyName is not null && CompactLeafPropertyNames.Contains(propertyName) && block.Properties.Count <= 4)
            {
                return true;
            }

            // 2. All keys belong to standard compact coordinate/dimension/stop keys (width/height, x/y, pos/color, horizontal/vertical)
            if (block.Properties.Count <= 4 && block.Properties.All(p => CompactLeafKeyNames.Contains(p.Name)))
            {
                return true;
            }

            // 3. Object inside an array (such as gradient stops or SDF shapes) or originally written on a single line
            var wasSingleLine = block.Span.Start.Line == block.Span.End.Line;
            var isArrayElement = propertyName is null;
            if (wasSingleLine || isArrayElement)
            {
                var preview = PreviewInlineObject(block);
                if (preview.Length <= 88)
                {
                    return true;
                }
            }

            return false;
        }

        private string PreviewInlineObject(ObjectBlockExpr block)
        {
            var tempState = new FormatterState(_source, [], []);
            tempState._sb.Append("{ ");
            for (var i = 0; i < block.Properties.Count; i++)
            {
                if (i > 0)
                {
                    tempState._sb.Append(", ");
                }

                var p = block.Properties[i];
                tempState._sb.Append(p.Name);
                tempState._sb.Append(" = ");
                tempState.FormatExpression(p.Value, 1, p.Name, null);
            }

            tempState._sb.Append(" }");
            return tempState._sb.ToString();
        }

        private void FormatArray(ArrayExpr array, int indentLevel, string? propertyName)
        {
            if (array.Elements.Count == 0 && !HasUnemittedCommentsInSpan(array.Span))
            {
                _sb.Append("[]");
                return;
            }

            var hasComments = HasUnemittedCommentsInSpan(array.Span);
            var hasObjectElements = array.Elements.Any(e => e is ObjectBlockExpr or ArrayExpr);
            var wasMultiLine = array.Span.Start.Line != array.Span.End.Line;

            if (!hasComments && !hasObjectElements && !wasMultiLine)
            {
                _sb.Append('[');
                for (var i = 0; i < array.Elements.Count; i++)
                {
                    if (i > 0)
                    {
                        _sb.Append(", ");
                    }

                    FormatExpression(array.Elements[i], indentLevel, null, propertyName);
                }

                _sb.Append(']');
                return;
            }

            // Check if a single-line array without objects is short enough even if originally multi-line
            if (!hasComments && !hasObjectElements && array.Elements.Count <= 6)
            {
                var tempState = new FormatterState(_source, [], []);
                tempState._sb.Append('[');
                for (var i = 0; i < array.Elements.Count; i++)
                {
                    if (i > 0)
                    {
                        tempState._sb.Append(", ");
                    }

                    tempState.FormatExpression(array.Elements[i], indentLevel, null, propertyName);
                }

                tempState._sb.Append(']');
                var inlineCandidate = tempState._sb.ToString();
                if (inlineCandidate.Length <= 72 && !wasMultiLine)
                {
                    _sb.Append(inlineCandidate);
                    return;
                }
            }

            _sb.Append("[\n");
            var childIndent = indentLevel + 1;
            var lastEndLine = array.Span.Start.Line;

            for (var i = 0; i < array.Elements.Count; i++)
            {
                var elem = array.Elements[i];
                EmitLeadingCommentsBefore(elem.Span.Start.Offset, childIndent, ref lastEndLine, maxOffset: array.Span.End.Offset);
                EnsureNewline();
                AppendIndent(childIndent);
                FormatExpression(elem, childIndent, null, propertyName);
                if (i < array.Elements.Count - 1)
                {
                    _sb.Append(',');
                }

                lastEndLine = GetActualEndLine(elem.Span);
                EmitTrailingCommentOnLine(lastEndLine);
            }

            EmitLeadingCommentsBefore(array.Span.End.Offset, childIndent, ref lastEndLine, maxOffset: array.Span.End.Offset);
            EnsureNewline();
            AppendIndent(indentLevel);
            _sb.Append(']');
        }

        private void FormatFunctionCall(FunctionCallExpr call, int indentLevel)
        {
            FormatExpression(call.Callee, indentLevel, null, null);
            _sb.Append('(');
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    _sb.Append(", ");
                }

                FormatExpression(call.Arguments[i], indentLevel, null, null);
            }

            _sb.Append(')');
        }

        private void FormatUnary(UnaryExpr unary, int indentLevel)
        {
            _sb.Append(unary.Operator == UnaryOperator.Minus ? '-' : '+');
            var wrapParens = unary.Operand is BinaryExpr or UnaryExpr;
            if (wrapParens)
            {
                _sb.Append('(');
            }

            FormatExpression(unary.Operand, indentLevel, null, null);
            if (wrapParens)
            {
                _sb.Append(')');
            }
        }

        private void FormatBinary(BinaryExpr binary, int indentLevel)
        {
            var parentPrec = GetPrecedence(binary.Operator);
            var wrapLeft = binary.Left is BinaryExpr leftBin && GetPrecedence(leftBin.Operator) < parentPrec;
            if (wrapLeft)
            {
                _sb.Append('(');
            }

            FormatExpression(binary.Left, indentLevel, null, null);
            if (wrapLeft)
            {
                _sb.Append(')');
            }

            _sb.Append(binary.Operator switch
            {
                BinaryOperator.Add => " + ",
                BinaryOperator.Subtract => " - ",
                BinaryOperator.Multiply => " * ",
                BinaryOperator.Divide => " / ",
                BinaryOperator.Modulo => " % ",
                _ => " + "
            });

            var wrapRight = binary.Right is BinaryExpr rightBin &&
                (GetPrecedence(rightBin.Operator) < parentPrec ||
                 (GetPrecedence(rightBin.Operator) == parentPrec &&
                  binary.Operator is BinaryOperator.Subtract or BinaryOperator.Divide or BinaryOperator.Modulo));

            if (wrapRight)
            {
                _sb.Append('(');
            }

            FormatExpression(binary.Right, indentLevel, null, null);
            if (wrapRight)
            {
                _sb.Append(')');
            }
        }

        private static int GetPrecedence(BinaryOperator op) => op switch
        {
            BinaryOperator.Add or BinaryOperator.Subtract => 10,
            BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo => 20,
            _ => 0
        };

        private string FormatNumberLiteral(NumberLiteralExpr number)
        {
            var start = number.Span.Start.Offset;
            var end = number.Span.End.Offset;
            if (start >= 0 && end > start && end <= _source.Length)
            {
                var raw = _source[start..end].Trim();
                if (raw.StartsWith('.'))
                {
                    return "0" + raw;
                }

                if (raw.Length > 0 && (char.IsDigit(raw[0]) || raw[0] is '-' or '+'))
                {
                    return raw;
                }
            }

            return number.IsInteger
                ? ((long)Math.Round(number.Value)).ToString(CultureInfo.InvariantCulture)
                : number.Value.ToString("G", CultureInfo.InvariantCulture);
        }

        private static string FormatStringLiteral(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            AppendEscapedStringText(sb, value, escapeInterpolation: true);
            sb.Append('"');
            return sb.ToString();
        }

        private void FormatInterpolatedString(InterpolatedStringExpr interpolated, int indentLevel)
        {
            _sb.Append('"');
            foreach (var part in interpolated.Parts)
            {
                switch (part)
                {
                    case InterpolatedTextPart textPart:
                        AppendEscapedStringText(_sb, textPart.Text, escapeInterpolation: true);
                        break;

                    case InterpolatedExpressionPart exprPart:
                        _sb.Append("${");
                        FormatExpression(exprPart.Expression, indentLevel, null, null);
                        if (!string.IsNullOrEmpty(exprPart.Format))
                        {
                            _sb.Append(':');
                            _sb.Append(exprPart.Format);
                        }

                        _sb.Append('}');
                        break;
                }
            }

            _sb.Append('"');
        }

        private static void AppendEscapedStringText(StringBuilder sb, string text, bool escapeInterpolation)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                switch (c)
                {
                    case '\\':
                        sb.Append(@"\\");
                        break;
                    case '"':
                        sb.Append(@"\""");
                        break;
                    case '\n':
                        sb.Append(@"\n");
                        break;
                    case '\r':
                        sb.Append(@"\r");
                        break;
                    case '\t':
                        sb.Append(@"\t");
                        break;
                    case '$' when escapeInterpolation && i + 1 < text.Length && text[i + 1] == '{':
                        sb.Append(@"\$");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
        }

        private bool HasUnemittedCommentsInSpan(SourceSpan span)
        {
            for (var i = 0; i < _comments.Count; i++)
            {
                if (!_commentEmitted[i] &&
                    _comments[i].StartOffset >= span.Start.Offset &&
                    _comments[i].EndOffset <= span.End.Offset)
                {
                    return true;
                }
            }

            return false;
        }

        private void EmitLeadingCommentsBefore(int targetOffset, int indentLevel, ref int lastEndLine, int maxOffset = int.MaxValue)
        {
            for (var i = 0; i < _comments.Count; i++)
            {
                if (_commentEmitted[i])
                {
                    continue;
                }

                var comment = _comments[i];
                if (comment.StartOffset >= targetOffset || comment.StartOffset >= maxOffset)
                {
                    break;
                }

                if (comment.IsTrailing && comment.StartLine == lastEndLine)
                {
                    _sb.Append(' ');
                    _sb.Append(comment.Text);
                    _commentEmitted[i] = true;
                    lastEndLine = comment.EndLine;
                    continue;
                }

                if (lastEndLine > 0)
                {
                    if (HasBlankLineBetween(lastEndLine, comment.StartLine) && !EndsWithBlankLine())
                    {
                        EnsureNewline();
                        _sb.Append('\n');
                    }
                    else
                    {
                        EnsureNewline();
                    }
                }

                AppendIndent(indentLevel);
                _sb.Append(comment.Text);
                _commentEmitted[i] = true;
                lastEndLine = comment.EndLine;
            }
        }

        private void EmitTrailingCommentOnLine(int line)
        {
            for (var i = 0; i < _comments.Count; i++)
            {
                if (_commentEmitted[i])
                {
                    continue;
                }

                var comment = _comments[i];
                if (comment.StartLine == line && comment.IsTrailing)
                {
                    _sb.Append(' ');
                    _sb.Append(comment.Text);
                    _commentEmitted[i] = true;
                }
            }
        }

        private void EmitRemainingComments(int indentLevel, ref int lastEndLine)
        {
            EmitLeadingCommentsBefore(int.MaxValue, indentLevel, ref lastEndLine);
        }

        private bool HasBlankLineBetween(int endLineA, int startLineB)
        {
            if (endLineA <= 0 || startLineB <= endLineA + 1)
            {
                return false;
            }

            for (var line = endLineA + 1; line < startLineB; line++)
            {
                if (_blankLines.Contains(line))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetActualEndLine(SourceSpan span)
        {
            // When a token or block ends at column 1 of the next line due to newline consumption, use Start.Line if single line
            if (span.End.Column == 1 && span.End.Line > span.Start.Line)
            {
                return span.End.Line - 1;
            }

            return span.End.Line;
        }

        private void AppendIndent(int indentLevel)
        {
            if (indentLevel > 0)
            {
                _sb.Append(' ', indentLevel * 2);
            }
        }

        private void EnsureNewline()
        {
            if (_sb.Length > 0 && _sb[^1] != '\n')
            {
                _sb.Append('\n');
            }
        }

        private bool EndsWithBlankLine() =>
            _sb.Length >= 2 && _sb[^1] == '\n' && _sb[^2] == '\n';
    }
}
