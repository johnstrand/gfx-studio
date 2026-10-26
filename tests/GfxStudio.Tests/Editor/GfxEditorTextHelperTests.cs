using FluentAssertions;
using GfxStudio.Editor;

namespace GfxStudio.Tests.Editor;

public sealed class GfxEditorTextHelperTests
{
    [Fact]
    public void HandleEnter_SmartBlockExpansion_SplitsBracesWithIndentedMiddle()
    {
        var text = "layers = {}";
        var caret = 10; // between { and }
        var (newText, newCaret) = GfxEditorTextHelper.HandleEnter(text, caret, caret, caret);

        newText.Should().Be("layers = {\n  \n}");
        newCaret.Should().Be(13); // on the middle line right after 2 spaces
    }

    [Fact]
    public void HandleEnter_NestedSmartBlockExpansion_PreservesAndExpandsIndentation()
    {
        var text = "  clouds = {}";
        var caret = 12; // between { and }
        var (newText, newCaret) = GfxEditorTextHelper.HandleEnter(text, caret, caret, caret);

        newText.Should().Be("  clouds = {\n    \n  }");
        newCaret.Should().Be(17); // after 4 spaces
    }

    [Fact]
    public void HandleEnter_OpenBraceAtEndOfLine_IndentsByTwoSpaces()
    {
        var text = "layers = {";
        var caret = text.Length;
        var (newText, newCaret) = GfxEditorTextHelper.HandleEnter(text, caret, caret, caret);

        newText.Should().Be("layers = {\n  ");
        newCaret.Should().Be(newText.Length);
    }

    [Fact]
    public void HandleEnter_RegularLine_PreservesCurrentIndentation()
    {
        var text = "  color = rgb(1, 2, 3)";
        var caret = text.Length;
        var (newText, newCaret) = GfxEditorTextHelper.HandleEnter(text, caret, caret, caret);

        newText.Should().Be("  color = rgb(1, 2, 3)\n  ");
        newCaret.Should().Be(newText.Length);
    }

    [Fact]
    public void HandleBackspace_EmptyBracePair_DeletesBoth()
    {
        var text = "layers = {}";
        var caret = 10; // between { and }
        var (handled, newText, newCaret) = GfxEditorTextHelper.HandleBackspace(text, caret, caret, caret);

        handled.Should().BeTrue();
        newText.Should().Be("layers = ");
        newCaret.Should().Be(9);
    }

    [Fact]
    public void HandleBackspace_EmptyQuotePair_DeletesBoth()
    {
        var text = "filename = \"\"";
        var caret = 12; // between ""
        var (handled, newText, newCaret) = GfxEditorTextHelper.HandleBackspace(text, caret, caret, caret);

        handled.Should().BeTrue();
        newText.Should().Be("filename = ");
        newCaret.Should().Be(11);
    }

    [Fact]
    public void HandleTextInput_OpenBrace_InsertsPair()
    {
        var text = "layers = ";
        var (handled, newText, newCaret, _, _) = GfxEditorTextHelper.HandleTextInput(text, text.Length, text.Length, '{');

        handled.Should().BeTrue();
        newText.Should().Be("layers = {}");
        newCaret.Should().Be(10);
    }

    [Fact]
    public void HandleTextInput_OpenBracket_WrapsSelection()
    {
        var text = "effects = foo";
        var selStart = 10;
        var selEnd = 13;
        var (handled, newText, newCaret, newSelStart, newSelEnd) = GfxEditorTextHelper.HandleTextInput(text, selStart, selEnd, '[');

        handled.Should().BeTrue();
        newText.Should().Be("effects = [foo]");
        newSelStart.Should().Be(11);
        newSelEnd.Should().Be(14);
    }

    [Fact]
    public void HandleTextInput_ClosingBrace_OvertypesWhenAtCaret()
    {
        var text = "layers = {}";
        var caret = 10; // right before '}'
        var (handled, newText, newCaret, _, _) = GfxEditorTextHelper.HandleTextInput(text, caret, caret, '}');

        handled.Should().BeTrue();
        newText.Should().Be("layers = {}");
        newCaret.Should().Be(11); // stepped over
    }

    [Fact]
    public void HandleTab_SingleCursor_InsertsTwoSpaces()
    {
        var text = "color = rgb(1, 2, 3)";
        var caret = 0;
        var (newText, newStart, newEnd) = GfxEditorTextHelper.HandleTab(text, caret, caret, isShift: false);

        newText.Should().Be("  color = rgb(1, 2, 3)");
        newStart.Should().Be(2);
        newEnd.Should().Be(2);
    }

    [Fact]
    public void HandleTab_MultiLineSelection_IndentsAndOutdents()
    {
        var text = "line1\nline2";
        var (indented, _, _) = GfxEditorTextHelper.HandleTab(text, 0, text.Length, isShift: false);
        indented.Should().Be("  line1\n  line2");

        var (outdented, _, _) = GfxEditorTextHelper.HandleTab(indented, 0, indented.Length, isShift: true);
        outdented.Should().Be("line1\nline2");
    }

    [Fact]
    public void ToggleComment_CommentsAndUncommentsLines()
    {
        var text = "type = fill\ncolor = rgb(0, 0, 0)";
        var (commented, _, _) = GfxEditorTextHelper.ToggleComment(text, 0, text.Length);
        commented.Should().Be("# type = fill\n# color = rgb(0, 0, 0)");

        var (uncommented, _, _) = GfxEditorTextHelper.ToggleComment(commented, 0, commented.Length);
        uncommented.Should().Be("type = fill\ncolor = rgb(0, 0, 0)");
    }
}
