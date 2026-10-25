using FluentAssertions;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests.Rendering;

public sealed class ShapeAndGradientLayerTests
{
    [Fact]
    public void RectLayer_RendersFillAndStrokeWithRoundedCorners()
    {
        const string source = """
            layers = {
              box = {
                type = rect
                size = { width = 12, height = 12 }
                corner_radius = 6
                fill_color = rgb(255, 0, 0)
                stroke_color = rgb(0, 0, 255)
                stroke_width = 2
              }
            }
            output = { dimensions = { width = 12, height = 12 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap!.GetPixel(6, 6).Should().Be(new SKColor(255, 0, 0));
        result.Bitmap.GetPixel(0, 6).Blue.Should().BeGreaterThan(200);
        result.Bitmap.GetPixel(0, 0).Alpha.Should().Be(0);
    }

    [Fact]
    public void CircleAndEllipseLayers_RenderFilledShapes()
    {
        const string source = """
            layers = {
              circle = { type = circle, center = { x = 5, y = 5 }, radius = 3, color = rgb(255, 0, 0) }
              ellipse = { type = ellipse, center = { x = 15, y = 5 }, radii = { x = 4, y = 2 }, color = rgb(0, 255, 0) }
            }
            output = { dimensions = { width = 20, height = 10 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap!.GetPixel(5, 5).Red.Should().Be(255);
        result.Bitmap.GetPixel(15, 5).Green.Should().Be(255);
        result.Bitmap.GetPixel(15, 1).Alpha.Should().Be(0);
    }

    [Fact]
    public void LineLayer_RendersConfiguredSegmentAndStrokeWidth()
    {
        const string source = """
            layers = {
              diagonal = {
                type = line
                start = { x = 1, y = 1 }
                end = { x = 7, y = 7 }
                color = rgb(20, 40, 60)
                width = 2
              }
            }
            output = { dimensions = { width = 8, height = 8 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap!.GetPixel(4, 4).Should().Be(new SKColor(20, 40, 60));
        result.Bitmap.GetPixel(0, 7).Alpha.Should().Be(0);
    }

    [Fact]
    public void PolygonAndStarLayers_RenderExplicitAndGeneratedPaths()
    {
        const string source = """
            layers = {
              triangle = {
                type = polygon
                points = [{ x = 2, y = 2 }, { x = 14, y = 2 }, { x = 8, y = 14 }]
                fill_color = rgb(255, 0, 0)
              }
              star = {
                type = star
                center = { x = 22, y = 8 }
                sides = 5
                radius = 7
                inner_radius = 3
                fill_color = rgb(0, 0, 255)
              }
            }
            output = { dimensions = { width = 30, height = 16 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap!.GetPixel(8, 5).Should().Be(new SKColor(255, 0, 0));
        result.Bitmap.GetPixel(22, 3).Blue.Should().Be(255);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("conic")]
    public void GradientLayer_RendersAllSupportedStyles(string style)
    {
        var source = $$"""
            layers = {
              gradient = {
                type = gradient
                style = {{style}}
                color = color_ramp(rgb(255, 0, 0), rgb(0, 0, 255))
              }
            }
            output = { dimensions = { width = 8, height = 8 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap.Should().NotBeNull();
        result.Bitmap!.GetPixel(4, 4).Should().NotBe(result.Bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void GradientLayer_ReportsMissingColorRamp()
    {
        const string source = """
            layers = {
              gradient = {
                type = gradient
                color = rgb(255, 0, 0)
              }
            }
            output = { dimensions = { width = 4, height = 4 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "RND047");
    }
}
