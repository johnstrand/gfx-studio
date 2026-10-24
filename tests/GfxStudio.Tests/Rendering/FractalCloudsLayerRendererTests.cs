using FluentAssertions;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests.Rendering;

public sealed class FractalCloudsLayerRendererTests
{
    [Fact]
    public void RenderSource_NumberGfxLayer0AndLayer1_RendersCyanMultipliedFractalClouds()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
                color = rgb(0, 255, 255)
              }
              layer1 = {
                type = fractal_clouds
                color = color_ramp(NAMED_COLORS.WHITE, NAMED_COLORS.BLACK)
                mix = multiply
                size = output.dimensions
              }
            }

            output = {
              dimensions = { width = 64, height = 64 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, masterSeed: 42);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();
        result.Bitmap.Should().NotBeNull();
        result.Bitmap!.Width.Should().Be(64);
        result.Bitmap.Height.Should().Be(64);

        var distinctValues = new HashSet<byte>();
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var pixel = result.Bitmap.GetPixel(x, y);
                // Multiplied over rgb(0, 255, 255) by grayscale (v, v, v) -> (0, v, v, 255)
                pixel.Red.Should().Be(0);
                pixel.Green.Should().Be(pixel.Blue);
                pixel.Alpha.Should().Be(255);
                distinctValues.Add(pixel.Green);
            }
        }

        // Verify the fractal cloud texture spans a wide variety of intensity values
        distinctValues.Count.Should().BeGreaterThan(30);
    }

    [Fact]
    public void RenderSource_IsByteForByteDeterministicForSameSeedAndVariesForDifferentSeeds()
    {
        const string source = """
            layers = {
              clouds = {
                type = fractal_clouds
                color = color_ramp(NAMED_COLORS.WHITE, NAMED_COLORS.BLACK)
                size = output.dimensions
              }
            }

            output = {
              dimensions = { width = 32, height = 32 }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var runA = compositor.RenderSource(source, masterSeed: 123456);
        using var runB = compositor.RenderSource(source, masterSeed: 123456);
        using var runC = compositor.RenderSource(source, masterSeed: 654321);

        var bytesA = runA.Bitmap!.GetPixelSpan().ToArray();
        var bytesB = runB.Bitmap!.GetPixelSpan().ToArray();
        var bytesC = runC.Bitmap!.GetPixelSpan().ToArray();

        bytesA.Should().Equal(bytesB);
        bytesA.Should().NotEqual(bytesC);
    }

    [Fact]
    public void RenderSource_LoopSeamless_ProducesIdenticalFramesAtPhaseZeroAndOne()
    {
        const string sourceAtZero = """
            layers = {
              clouds = {
                type = fractal_clouds
                color = color_ramp(NAMED_COLORS.WHITE, NAMED_COLORS.BLACK)
                loop_seamless = true
                phase = 0.0
              }
            }
            output = { dimensions = { width = 24, height = 24 } }
            """;

        const string sourceAtHalf = """
            layers = {
              clouds = {
                type = fractal_clouds
                color = color_ramp(NAMED_COLORS.WHITE, NAMED_COLORS.BLACK)
                loop_seamless = true
                phase = 0.5
              }
            }
            output = { dimensions = { width = 24, height = 24 } }
            """;

        const string sourceAtOne = """
            layers = {
              clouds = {
                type = fractal_clouds
                color = color_ramp(NAMED_COLORS.WHITE, NAMED_COLORS.BLACK)
                loop_seamless = true
                phase = 1.0
              }
            }
            output = { dimensions = { width = 24, height = 24 } }
            """;

        var compositor = new GfxCanvasCompositor();
        using var frame0 = compositor.RenderSource(sourceAtZero, masterSeed: 777);
        using var frameHalf = compositor.RenderSource(sourceAtHalf, masterSeed: 777);
        using var frame1 = compositor.RenderSource(sourceAtOne, masterSeed: 777);

        var bytes0 = frame0.Bitmap!.GetPixelSpan().ToArray();
        var bytesHalf = frameHalf.Bitmap!.GetPixelSpan().ToArray();
        var bytes1 = frame1.Bitmap!.GetPixelSpan().ToArray();

        bytes0.Should().Equal(bytes1);
        bytes0.Should().NotEqual(bytesHalf);
    }

    [Fact]
    public void RenderSource_SupportsCustomOctavesScalePersistenceAndLacunarity()
    {
        const string source = """
            layers = {
              clouds = {
                type = fractal_clouds
                color = color_ramp(rgb(255, 0, 0), rgb(0, 0, 255))
                octaves = 8
                scale = 6.0
                persistence = 0.6
                lacunarity = 2.2
              }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, masterSeed: 99);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();
        var centerPixel = result.Bitmap!.GetPixel(8, 8);
        centerPixel.Green.Should().Be(0);
        (centerPixel.Red + centerPixel.Blue).Should().BeInRange(254, 256);
    }
}
