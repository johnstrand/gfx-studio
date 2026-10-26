using System.Text;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Parsing.Ast;

namespace GfxStudio.Editor;

/// <summary>
/// Pure text manipulation helper for code-editor quality-of-life behaviors
/// (auto-indent, brace/bracket/quote completion, tab/shift-tab, comment toggling, and layer insertion).
/// </summary>
public static class GfxEditorTextHelper
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="name"/> is a non-empty valid <c>.gfx</c> identifier (<c>[a-zA-Z_][a-zA-Z0-9_]*</c>).
    /// </summary>
    public static bool IsValidIdentifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Generates a formatted layer block and inserts it inside the top-level <c>layers = { ... }</c> block
    /// (or creates a <c>layers</c> block if none exists).
    /// </summary>
    public static (string NewText, int NewCaret) InsertLayerBlock(
        string? documentText,
        string layerName,
        string layerType,
        IReadOnlyList<(string Key, string Value)> properties,
        IReadOnlyList<(string Key, string Value)>? transformProperties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerType);

        var text = documentText ?? string.Empty;
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var sb = new StringBuilder();
        sb.Append("  ").Append(layerName).Append(" = {").Append(nl);
        sb.Append("    type = ").Append(layerType).Append(nl);

        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    sb.Append("    ").Append(key.Trim()).Append(" = ").Append(value.Trim()).Append(nl);
                }
            }
        }

        var nonEmptyTransforms = transformProperties?
            .Where(t => !string.IsNullOrWhiteSpace(t.Key) && !string.IsNullOrWhiteSpace(t.Value))
            .ToList();

        if (nonEmptyTransforms is { Count: > 0 })
        {
            sb.Append("    transform = {").Append(nl);
            foreach (var (key, value) in nonEmptyTransforms)
            {
                sb.Append("      ").Append(key.Trim()).Append(" = ").Append(value.Trim()).Append(nl);
            }

            sb.Append("    }").Append(nl);
        }

        sb.Append("  }");
        var layerSnippet = sb.ToString();

        var parseResult = GfxParser.Parse(text);
        var layersProp = parseResult.Document?.Properties
            .FirstOrDefault(p => string.Equals(p.Name, "layers", StringComparison.Ordinal) && p.Value is ObjectBlockExpr);

        if (layersProp?.Value is ObjectBlockExpr layersBlock)
        {
            var closeBraceIndex = FindClosingBraceOffset(text, layersBlock.Span);
            if (closeBraceIndex >= 0)
            {
                // Trim trailing horizontal whitespace before '}' on the closing line
                var insertPos = closeBraceIndex;
                while (insertPos > 0 && (text[insertPos - 1] == ' ' || text[insertPos - 1] == '\t'))
                {
                    insertPos--;
                }

                var needsLeadingNewline = insertPos > 0 && text[insertPos - 1] is not '\n' and not '\r';
                var insertion = (needsLeadingNewline ? nl : string.Empty) + layerSnippet + nl;
                var updated = text.Remove(insertPos, closeBraceIndex - insertPos).Insert(insertPos, insertion);
                var caret = insertPos + insertion.Length;
                return (updated, caret);
            }
        }

        // Fallback: no top-level 'layers' block exists yet — create one at the top of the document
        var newLayersBlock = $"layers = {{{nl}{layerSnippet}{nl}}}{nl}";
        if (string.IsNullOrWhiteSpace(text))
        {
            return (newLayersBlock, newLayersBlock.Length);
        }

        var combined = newLayersBlock + nl + text.TrimStart('\r', '\n');
        return (combined, newLayersBlock.Length);
    }

    private static int FindClosingBraceOffset(string text, GfxStudio.Core.Diagnostics.SourceSpan span)
    {
        var searchEnd = Math.Clamp(span.End.Offset, 0, text.Length);
        for (var i = searchEnd - 1; i >= Math.Max(0, span.Start.Offset); i--)
        {
            if (text[i] == '}')
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Handles Enter key: preserves leading indentation and performs smart block expansion when between braces/brackets.
    /// </summary>
    public static (string NewText, int NewCaret) HandleEnter(string text, int caretIndex, int selStart, int selEnd)
    {
        text ??= string.Empty;
        var minSel = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
        var maxSel = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

        if (minSel != maxSel)
        {
            text = text.Remove(minSel, maxSel - minSel);
            caretIndex = minSel;
        }

        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        // Find start of current line
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caretIndex - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineBeforeCaret = text[lineStart..caretIndex];

        // Compute leading indentation
        var indentLen = 0;
        while (indentLen < lineBeforeCaret.Length && (lineBeforeCaret[indentLen] == ' ' || lineBeforeCaret[indentLen] == '\t'))
        {
            indentLen++;
        }
        var indent = lineBeforeCaret[..indentLen];

        // Non-whitespace character before caret on this line
        var charBefore = '\0';
        for (var i = caretIndex - 1; i >= lineStart; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                charBefore = text[i];
                break;
            }
        }

        // Non-whitespace character after caret on this line
        var charAfter = '\0';
        for (var i = caretIndex; i < text.Length && text[i] != '\r' && text[i] != '\n'; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                charAfter = text[i];
                break;
            }
        }

        // 1. Smart Block Expansion: between '{' and '}' or '[' and ']'
        var isBraceBlock = charBefore == '{' && charAfter == '}';
        var isBracketBlock = charBefore == '[' && charAfter == ']';
        if (isBraceBlock || isBracketBlock)
        {
            var insert = nl + indent + "  " + nl + indent;
            var newText = text.Insert(caretIndex, insert);
            var newCaret = caretIndex + nl.Length + indent.Length + 2;
            return (newText, newCaret);
        }

        // 2. Open block: line before ends with '{' or '['
        if (charBefore is '{' or '[')
        {
            var insert = nl + indent + "  ";
            var newText = text.Insert(caretIndex, insert);
            var newCaret = caretIndex + insert.Length;
            return (newText, newCaret);
        }

        // 3. Regular newline preserving indentation
        {
            var insert = nl + indent;
            var newText = text.Insert(caretIndex, insert);
            var newCaret = caretIndex + insert.Length;
            return (newText, newCaret);
        }
    }

    /// <summary>
    /// Handles Backspace key: deletes paired empty delimiters ('{}', '[]', '()', '""') in a single stroke.
    /// </summary>
    public static (bool Handled, string NewText, int NewCaret) HandleBackspace(string text, int caretIndex, int selStart, int selEnd)
    {
        text ??= string.Empty;
        var minSel = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
        var maxSel = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

        if (minSel != maxSel || caretIndex <= 0 || caretIndex >= text.Length)
        {
            return (false, text, caretIndex);
        }

        var prev = text[caretIndex - 1];
        var next = text[caretIndex];

        var isPair = (prev == '{' && next == '}')
            || (prev == '[' && next == ']')
            || (prev == '(' && next == ')')
            || (prev == '"' && next == '"');

        if (isPair)
        {
            var newText = text.Remove(caretIndex - 1, 2);
            return (true, newText, caretIndex - 1);
        }

        return (false, text, caretIndex);
    }

    /// <summary>
    /// Handles text input: delimiter overtyping, empty pair insertion, and selection wrapping.
    /// </summary>
    public static (bool Handled, string NewText, int NewCaret, int NewSelStart, int NewSelEnd) HandleTextInput(
        string text,
        int selStart,
        int selEnd,
        char ch)
    {
        text ??= string.Empty;
        var minSel = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
        var maxSel = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

        // 1. Overtype closing character
        if (ch is '}' or ']' or ')' or '"')
        {
            if (minSel == maxSel && minSel < text.Length && text[minSel] == ch)
            {
                var nextPos = minSel + 1;
                return (true, text, nextPos, nextPos, nextPos);
            }
        }

        // 2. Auto-closing pair insertion or selection wrapping
        if (ch is '{' or '[' or '(' or '"')
        {
            var closeChar = ch switch
            {
                '{' => '}',
                '[' => ']',
                '(' => ')',
                '"' => '"',
                _ => '\0'
            };

            // Selection wrapping: wrap selected text
            if (minSel != maxSel)
            {
                var selected = text[minSel..maxSel];
                var wrapped = ch + selected + closeChar;
                var newText = text[..minSel] + wrapped + text[maxSel..];
                var newSelStart = minSel + 1;
                var newSelEnd = minSel + 1 + selected.Length;
                return (true, newText, newSelEnd, newSelStart, newSelEnd);
            }

            // Standalone quote check: don't auto-pair quotes if preceded by or followed by a word character
            if (ch == '"' && minSel < text.Length && (char.IsLetterOrDigit(text[minSel]) || text[minSel] == '_'))
            {
                return (false, text, minSel, minSel, minSel);
            }

            // Standalone pair insertion
            {
                var pair = $"{ch}{closeChar}";
                var newText = text.Insert(minSel, pair);
                var newPos = minSel + 1;
                return (true, newText, newPos, newPos, newPos);
            }
        }

        return (false, text, minSel, minSel, maxSel);
    }

    /// <summary>
    /// Handles Tab and Shift+Tab indentation/outdentation.
    /// </summary>
    public static (string NewText, int NewSelStart, int NewSelEnd) HandleTab(
        string text,
        int selStart,
        int selEnd,
        bool isShift)
    {
        text ??= string.Empty;
        var minSel = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
        var maxSel = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

        var isMultiLine = minSel != maxSel && text[minSel..maxSel].Contains('\n', StringComparison.Ordinal);

        // Simple Tab insertion with no multi-line selection
        if (!isShift && !isMultiLine)
        {
            if (minSel != maxSel)
            {
                text = text.Remove(minSel, maxSel - minSel);
            }

            var newText = text.Insert(minSel, "  ");
            return (newText, minSel + 2, minSel + 2);
        }

        // Multi-line indent or Shift+Tab outdent
        var lineStart = text.LastIndexOf('\n', Math.Max(0, minSel - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var lineEnd = maxSel < text.Length ? text.IndexOf('\n', maxSel) : text.Length;
        if (lineEnd < 0) lineEnd = text.Length;

        // If selection ends right at the start of a newline, don't include the trailing line
        if (maxSel > minSel && text[maxSel - 1] == '\n' && maxSel < lineEnd)
        {
            lineEnd = maxSel - 1;
        }

        var block = text[lineStart..lineEnd];
        var lines = block.Split('\n');
        var updatedLines = new List<string>(lines.Length);
        var firstLineDelta = 0;
        var totalDelta = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!isShift)
            {
                // Indent by 2 spaces
                updatedLines.Add("  " + line);
                if (i == 0) firstLineDelta = 2;
                totalDelta += 2;
            }
            else
            {
                // Outdent up to 2 leading spaces or 1 tab
                var strip = 0;
                if (line.StartsWith("  ", StringComparison.Ordinal)) strip = 2;
                else if (line.StartsWith(" ", StringComparison.Ordinal)) strip = 1;
                else if (line.StartsWith("\t", StringComparison.Ordinal)) strip = 1;

                updatedLines.Add(line[strip..]);
                if (i == 0) firstLineDelta = -strip;
                totalDelta -= strip;
            }
        }

        var updatedBlock = string.Join("\n", updatedLines);
        var resultText = text[..lineStart] + updatedBlock + text[lineEnd..];
        var newStart = Math.Max(lineStart, minSel + firstLineDelta);
        var newEnd = Math.Max(newStart, maxSel + totalDelta);
        return (resultText, newStart, newEnd);
    }

    /// <summary>
    /// Handles Ctrl+/ line comment toggling on current line or multi-line selection.
    /// </summary>
    public static (string NewText, int NewSelStart, int NewSelEnd) ToggleComment(
        string text,
        int selStart,
        int selEnd)
    {
        text ??= string.Empty;
        var minSel = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
        var maxSel = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

        var lineStart = text.LastIndexOf('\n', Math.Max(0, minSel - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var lineEnd = maxSel < text.Length ? text.IndexOf('\n', maxSel) : text.Length;
        if (lineEnd < 0) lineEnd = text.Length;

        if (maxSel > minSel && text[maxSel - 1] == '\n' && maxSel < lineEnd)
        {
            lineEnd = maxSel - 1;
        }

        var block = text[lineStart..lineEnd];
        var lines = block.Split('\n');

        // Check if all non-empty lines start with #
        var allCommented = true;
        var nonBlankCount = 0;
        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimStart();
            if (trimmed.Length == 0) continue;
            nonBlankCount++;
            if (!trimmed.StartsWith('#'))
            {
                allCommented = false;
                break;
            }
        }

        if (nonBlankCount == 0) allCommented = false;

        var updatedLines = new List<string>(lines.Length);
        var totalDelta = 0;
        var firstLineDelta = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (allCommented)
            {
                // Uncomment: remove '# ' or '#'
                var indentCount = 0;
                while (indentCount < line.Length && (line[indentCount] == ' ' || line[indentCount] == '\t'))
                {
                    indentCount++;
                }

                var remainder = line[indentCount..];
                if (remainder.StartsWith("# ", StringComparison.Ordinal))
                {
                    var updated = line[..indentCount] + remainder[2..];
                    updatedLines.Add(updated);
                    var delta = -2;
                    if (i == 0) firstLineDelta = delta;
                    totalDelta += delta;
                }
                else if (remainder.StartsWith('#'))
                {
                    var updated = line[..indentCount] + remainder[1..];
                    updatedLines.Add(updated);
                    var delta = -1;
                    if (i == 0) firstLineDelta = delta;
                    totalDelta += delta;
                }
                else
                {
                    updatedLines.Add(line);
                }
            }
            else
            {
                // Comment: if line has content or if single empty line, prepend '# '
                if (lines.Length == 1 || line.Trim().Length > 0)
                {
                    var indentCount = 0;
                    while (indentCount < line.Length && (line[indentCount] == ' ' || line[indentCount] == '\t'))
                    {
                        indentCount++;
                    }

                    var updated = line[..indentCount] + "# " + line[indentCount..];
                    updatedLines.Add(updated);
                    var delta = 2;
                    if (i == 0) firstLineDelta = delta;
                    totalDelta += delta;
                }
                else
                {
                    updatedLines.Add(line);
                }
            }
        }

        var updatedBlock = string.Join("\n", updatedLines);
        var resultText = text[..lineStart] + updatedBlock + text[lineEnd..];
        var newStart = Math.Max(lineStart, minSel + firstLineDelta);
        var newEnd = Math.Max(newStart, maxSel + totalDelta);
        return (resultText, newStart, newEnd);
    }
}
