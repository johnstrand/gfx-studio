using FluentAssertions;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests.Rendering;

public sealed class ProceduralPatternTests
{
    [Theory]
    [InlineData("f1", "euclidean")]
    [InlineData("f2", "manhattan")]
    [InlineData("f2_minus_f1", "chebyshev")]
    [InlineData("cell_value", "euclidean")]
    public void WorleyLayer_GeneratesDeterministicCellularAndVoronoiTextures(string feature, string metric)
    {
        var source = $$"""
            layers = {
              cells = {
                type = worley
                color = color_ramp(NAMED_COLORS.WHITE, NAMED_COLORS.BLACK)
                cell_size = 16
                jitter = 0.85
                feature = {{feature}}
                distance_metric = {{metric}}
              }
            }
            output = {
              dimensions = { width = 32, height = 32 }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var first = compositor.RenderSource(source, masterSeed: 777UL);
        using var second = compositor.RenderSource(source, masterSeed: 777UL);
        using var differentSeed = compositor.RenderSource(source, masterSeed: 888UL);

        first.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, first.Diagnostics));
        second.HasErrors.Should().BeFalse();

        var firstBytes = first.Bitmap!.GetPixelSpan().ToArray();
        var secondBytes = second.Bitmap!.GetPixelSpan().ToArray();
        var diffBytes = differentSeed.Bitmap!.GetPixelSpan().ToArray();

        firstBytes.Should().Equal(secondBytes);
        firstBytes.Should().NotEqual(diffBytes);

        // Verify non-uniform cellular variation across the 32x32 texture
        var distinctLuminances = Enumerable.Range(0, 32)
            .SelectMany(y => Enumerable.Range(0, 32).Select(x => first.Bitmap.GetPixel(x, y).Red))
            .Distinct()
            .Count();
        distinctLuminances.Should().BeGreaterThan(2);
    }

    [Theory]
    [InlineData("fractal_clouds")]
    [InlineData("perlin")]
    [InlineData("simplex")]
    [InlineData("worley")]
    public void NoiseLayers_WithDomainWarping_ProduceDeterministicWarpedTextures(string layerType)
    {
        var unwarpedSource = $$"""
            layers = {
              n = {
                type = {{layerType}}
                scale = 4.0
                warp_strength = 0.0
              }
            }
            output = {
              dimensions = { width = 24, height = 24 }
            }
            """;

        var warpedSource = $$"""
            layers = {
              n = {
                type = {{layerType}}
                scale = 4.0
                warp_strength = 1.25
                warp_scale = 3.0
              }
            }
            output = {
              dimensions = { width = 24, height = 24 }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var unwarped = compositor.RenderSource(unwarpedSource, masterSeed: 42UL);
        using var warped1 = compositor.RenderSource(warpedSource, masterSeed: 42UL);
        using var warped2 = compositor.RenderSource(warpedSource, masterSeed: 42UL);

        unwarped.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, unwarped.Diagnostics));
        warped1.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, warped1.Diagnostics));

        var warpedBytes1 = warped1.Bitmap!.GetPixelSpan().ToArray();
        var warpedBytes2 = warped2.Bitmap!.GetPixelSpan().ToArray();
        var unwarpedBytes = unwarped.Bitmap!.GetPixelSpan().ToArray();

        warpedBytes1.Should().Equal(warpedBytes2);
        warpedBytes1.Should().NotEqual(unwarpedBytes);
    }

    [Fact]
    public void CheckerboardLayer_RendersCrispAlternatingTiles()
    {
        const string source = """
            layers = {
              board = {
                type = checkerboard
                tile_size = { width = 8, height = 8 }
                color1 = NAMED_COLORS.RED
                color2 = NAMED_COLORS.BLUE
              }
            }
            output = {
              dimensions = { width = 16, height = 16 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));

        result.Bitmap!.GetPixel(2, 2).Should().Be(new SKColor(255, 0, 0, 255));
        result.Bitmap.GetPixel(10, 2).Should().Be(new SKColor(0, 0, 255, 255));
        result.Bitmap.GetPixel(2, 10).Should().Be(new SKColor(0, 0, 255, 255));
        result.Bitmap.GetPixel(10, 10).Should().Be(new SKColor(255, 0, 0, 255));
    }

    [Fact]
    public void StripesGridAndDotsLayers_RenderAccuratelyAlignedGeometricPatterns()
    {
        const string source = """
            layers = {
              stripes_layer = {
                type = stripes
                stripe_width = 4
                angle = 0
                color1 = NAMED_COLORS.WHITE
                color2 = NAMED_COLORS.BLACK
              }
              grid_layer = {
                type = grid
                cell_size = 8
                line_width = 1
                color = NAMED_COLORS.RED
                background = NAMED_COLORS.TRANSPARENT
              }
              dots_layer = {
                type = dots
                spacing = 16
                radius = 3
                color = NAMED_COLORS.GREEN
                background = NAMED_COLORS.TRANSPARENT
              }
            }
            output = {
              dimensions = { width = 16, height = 16 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));

        // Grid line at x = 0 or y = 0 should be RED (overridden only if covered by dot, which is centered at (8,8))
        result.Bitmap!.GetPixel(0, 0).Should().Be(new SKColor(255, 0, 0, 255));

        // Center of 16x16 cell at (8, 8) is covered by the GREEN dot of radius 3
        result.Bitmap.GetPixel(8, 8).Should().Be(new SKColor(0, 255, 0, 255));

        // At (2, 2): inside first vertical stripe (x = 0..3 -> WHITE), not on grid line, outside dot
        result.Bitmap.GetPixel(2, 2).Should().Be(new SKColor(255, 255, 255, 255));

        // At (5, 2): inside second vertical stripe (x = 4..7 -> BLACK), not on grid line, outside dot
        result.Bitmap.GetPixel(5, 2).Should().Be(new SKColor(0, 0, 0, 255));
    }
}
