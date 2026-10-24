using FluentAssertions;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests.Rendering;

public sealed class FillLayerRendererTests
{
    [Fact]
    public void RenderSource_DefaultRegistryRendersAllPixelsWithSolidRgbaColor()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
                color = rgba(20, 40, 60, 0.5)
              }
            }
            output = {
              dimensions = { width = 8, height = 6 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();
        result.Bitmap.Should().NotBeNull();
        result.Bitmap!.Width.Should().Be(8);
        result.Bitmap.Height.Should().Be(6);

        for (var y = 0; y < result.Bitmap.Height; y++)
        {
            for (var x = 0; x < result.Bitmap.Width; x++)
            {
                result.Bitmap.GetPixel(x, y).Should().Be(new SKColor(20, 40, 60, 128));
            }
        }
    }

    [Fact]
    public void RenderSource_RendersNumberGfxFillLayerAsCyan()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
                color = rgb(0, 255, 255)
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap.Should().NotBeNull();
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                result.Bitmap!.GetPixel(x, y).Should().Be(new SKColor(0, 255, 255, 255));
            }
        }
    }

    [Fact]
    public void RenderSource_AppliesCustomSizeAndOffset()
    {
        const string source = """
            layers = {
              box = {
                type = fill
                color = rgb(200, 100, 0)
                size = { width = 2, height = 3 }
                offset = { x = 1, y = 2 }
              }
            }
            output = {
              dimensions = { width = 6, height = 6 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap!.GetPixel(1, 2).Should().Be(new SKColor(200, 100, 0, 255));
        result.Bitmap.GetPixel(2, 4).Should().Be(new SKColor(200, 100, 0, 255));
        result.Bitmap.GetPixel(0, 0).Should().Be(new SKColor(0, 0, 0, 0));
        result.Bitmap.GetPixel(3, 5).Should().Be(new SKColor(0, 0, 0, 0));
    }

    [Fact]
    public void RenderSource_FillLayerSamplesColorRampAtStart()
    {
        const string source = """
            layers = {
              ramp = {
                type = fill
                color = color_ramp(NAMED_COLORS.RED, NAMED_COLORS.BLUE)
              }
            }
            output = {
              dimensions = { width = 2, height = 2 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap!.GetPixel(0, 0).Should().Be(new SKColor(255, 0, 0, 255));
    }

    [Fact]
    public void RenderSource_MissingColorReportsLayerSpanDiagnostic()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
              }
            }
            output = {
              dimensions = { width = 4, height = 4 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, filePath: "missing-color.gfx");

        result.HasErrors.Should().BeTrue();
        var diagnostic = result.Diagnostics.Should().ContainSingle(item => item.Code == "RND011").Subject;
        diagnostic.Message.Should().Contain("layer0").And.Contain("color");
        diagnostic.Span.FilePath.Should().Be("missing-color.gfx");
        diagnostic.Span.Start.Line.Should().Be(2);
    }

    [Fact]
    public void RenderSource_InvalidColorTypeReportsDiagnosticInsteadOfThrowing()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
                color = 123
              }
            }
            output = {
              dimensions = { width = 2, height = 2 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle(item => item.Code == "RND012");
    }
}
