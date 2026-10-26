using FluentAssertions;
using GfxStudio.Cli;
using GfxStudio.Core.Formatting;
using GfxStudio.Core.Rendering;
using GfxStudio.Editor;

namespace GfxStudio.Tests.Parsing;

public sealed class GfxFormatterTests
{
    [Fact]
    public void Format_NormalizesIndentationAssignmentsCompactLeafObjectsExpressionsAndPreservesComments()
    {
        const string unformatted = """
            # Top-level header comment
            variables:{
            base_size:64 // trailing comment on variable
            }



            layers = {
            # Background fill layer
            bg = {type:fill,color:rgb(10,20,30)}
              box={
                type = rect
                size = {
                  width = variables.base_size / 2
                  height = (10 + 6) * 2
                }
                transform = {
                  offset = { x = 4, y = 8 }
                  rotation = -15
                }
                color = hex("#ff4081")
              }
            }
            output={dimensions={width=64,height=64},filename="out_${run.number:0000}.png"}
            """;

        var result = GfxFormatter.Format(unformatted);
        result.HasErrors.Should().BeFalse();
        result.Changed.Should().BeTrue();

        const string expected = """
            # Top-level header comment
            variables = {
              base_size = 64 // trailing comment on variable
            }

            layers = {
              # Background fill layer
              bg = {
                type = fill
                color = rgb(10, 20, 30)
              }
              box = {
                type = rect
                size = { width = variables.base_size / 2, height = (10 + 6) * 2 }
                transform = {
                  offset = { x = 4, y = 8 }
                  rotation = -15
                }
                color = hex("#ff4081")
              }
            }

            output = {
              dimensions = { width = 64, height = 64 }
              filename = "out_${run.number:0000}.png"
            }
            """;

        result.FormattedText.Should().Be(expected.Replace("\r\n", "\n") + "\n");

        // Idempotency check
        var secondPass = GfxFormatter.Format(result.FormattedText);
        secondPass.HasErrors.Should().BeFalse();
        secondPass.Changed.Should().BeFalse();
        secondPass.FormattedText.Should().Be(result.FormattedText);

        // Semantic equivalence check: rendered bitmaps before and after formatting must be identical
        var compositor = new GfxCanvasCompositor();
        using var rBefore = compositor.RenderSource(unformatted, masterSeed: 42);
        using var rAfter = compositor.RenderSource(result.FormattedText, masterSeed: 42);
        rBefore.HasErrors.Should().BeFalse();
        rAfter.HasErrors.Should().BeFalse();
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                rAfter.Bitmap!.GetPixel(x, y).Should().Be(rBefore.Bitmap!.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Format_LeavesSyntacticallyInvalidSourceUnchanged()
    {
        const string invalid = "layers = { broken = { type = fill ";
        var result = GfxFormatter.Format(invalid);

        result.HasErrors.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.FormattedText.Should().Be(invalid);
    }

    [Fact]
    public async Task CliFormat_SupportsCheckAndInPlaceFormatting()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gfx_fmt_{Guid.NewGuid():N}.gfx");
        try
        {
            await File.WriteAllTextAsync(tempFile, "layers={bg={type=fill,color=NAMED_COLORS.RED}}\noutput={dimensions={width=8,height=8}}");

            var checkBefore = await GfxCli.InvokeAsync(["format", tempFile, "--check"]);
            checkBefore.Should().Be(1);

            var formatExit = await GfxCli.InvokeAsync(["format", tempFile]);
            formatExit.Should().Be(0);

            var checkAfter = await GfxCli.InvokeAsync(["format", tempFile, "--check"]);
            checkAfter.Should().Be(0);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void QuickAddLayerHelper_ValidatesIdentifiersAndInsertsIntoLayersBlock()
    {
        GfxEditorTextHelper.IsValidIdentifier("my_layer_1").Should().BeTrue();
        GfxEditorTextHelper.IsValidIdentifier("_bg").Should().BeTrue();
        GfxEditorTextHelper.IsValidIdentifier("").Should().BeFalse();
        GfxEditorTextHelper.IsValidIdentifier("1_bad").Should().BeFalse();
        GfxEditorTextHelper.IsValidIdentifier("bad-name").Should().BeFalse();

        const string initialDoc = """
            layers = {
              bg = {
                type = fill
                color = NAMED_COLORS.BLACK
              }
            }

            output = {
              dimensions = { width = 16, height = 16 }
            }
            """;

        var (updatedDoc, _) = GfxEditorTextHelper.InsertLayerBlock(
            initialDoc,
            "stars_top",
            "star",
            [("sides", "5"), ("color", "NAMED_COLORS.YELLOW")],
            [("offset", "{ x = 2, y = 2 }"), ("rotation", "15")]);

        updatedDoc.Should().Contain("stars_top = {");
        updatedDoc.Should().Contain("type = star");
        updatedDoc.Should().Contain("sides = 5");
        updatedDoc.Should().Contain("transform = {");
        updatedDoc.Should().Contain("rotation = 15");

        using var render = new GfxCanvasCompositor().RenderSource(updatedDoc);
        render.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, render.Diagnostics));
    }
}
