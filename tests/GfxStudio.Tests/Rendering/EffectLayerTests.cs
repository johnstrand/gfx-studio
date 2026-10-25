using FluentAssertions;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests.Rendering;

public sealed class EffectLayerTests
{
    [Fact]
    public void GaussianBlur_WorksAsStandaloneAdjustmentLayerAndInlineEffect()
    {
        const string adjustmentSource = """
            layers = {
              bg = {
                type = checkerboard
                tile_size = 8
                color1 = NAMED_COLORS.WHITE
                color2 = NAMED_COLORS.BLACK
              }
              blur_pass = {
                type = gaussian_blur
                radius = 2.5
              }
            }
            output = {
              dimensions = { width = 16, height = 16 }
            }
            """;

        const string inlineSource = """
            layers = {
              bg = {
                type = checkerboard
                tile_size = 8
                color1 = NAMED_COLORS.WHITE
                color2 = NAMED_COLORS.BLACK
                blur = { radius = 2.5 }
              }
            }
            output = {
              dimensions = { width = 16, height = 16 }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var adjustmentResult = compositor.RenderSource(adjustmentSource);
        using var inlineResult = compositor.RenderSource(inlineSource);

        adjustmentResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, adjustmentResult.Diagnostics));
        inlineResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, inlineResult.Diagnostics));

        // Both adjustment layer and inline effect produce identical blurred output
        adjustmentResult.Bitmap!.GetPixelSpan().ToArray()
            .Should().Equal(inlineResult.Bitmap!.GetPixelSpan().ToArray());

        // At tile boundary (7, 7), blurring mixes WHITE (255) and BLACK (0) into intermediate gray
        var boundaryRed = adjustmentResult.Bitmap.GetPixel(7, 7).Red;
        boundaryRed.Should().BeInRange(30, 225);
    }

    [Fact]
    public void ThresholdAndPosterize_WorkAsStandaloneAdjustmentAndPerLayerEffects()
    {
        const string thresholdSource = """
            layers = {
              grad = {
                type = gradient
                style = linear
                angle = 0
                color = color_ramp(NAMED_COLORS.BLACK, NAMED_COLORS.WHITE)
              }
              thresh_pass = {
                type = threshold
                level = 0.5
                low_color = NAMED_COLORS.BLUE
                high_color = NAMED_COLORS.RED
              }
            }
            output = {
              dimensions = { width = 16, height = 4 }
            }
            """;

        using var threshResult = new GfxCanvasCompositor().RenderSource(thresholdSource);
        threshResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, threshResult.Diagnostics));
        threshResult.Bitmap!.GetPixel(1, 2).Should().Be(new SKColor(0, 0, 255, 255));
        threshResult.Bitmap.GetPixel(14, 2).Should().Be(new SKColor(255, 0, 0, 255));

        const string posterizeSource = """
            layers = {
              grad = {
                type = gradient
                style = linear
                angle = 0
                color = color_ramp(NAMED_COLORS.BLACK, NAMED_COLORS.WHITE)
                posterize = { levels = 3 }
              }
            }
            output = {
              dimensions = { width = 32, height = 4 }
            }
            """;

        using var postResult = new GfxCanvasCompositor().RenderSource(posterizeSource);
        postResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, postResult.Diagnostics));

        var distinctReds = Enumerable.Range(0, 32)
            .Select(x => postResult.Bitmap!.GetPixel(x, 2).Red)
            .Distinct()
            .OrderBy(v => v)
            .ToArray();

        // 3 levels -> quantized to 0, 128, 255
        distinctReds.Should().Equal((byte)0, (byte)128, (byte)255);
    }

    [Fact]
    public void PixelateAndColorAdjust_WorkAsAdjustmentLayersAndEffectsArray()
    {
        const string source = """
            layers = {
              clouds = {
                type = fractal_clouds
                scale = 4.0
                effects = [
                  { type = pixelate, block_size = 4 },
                  { type = color_adjust, invert = true }
                ]
              }
            }
            output = {
              dimensions = { width = 16, height = 16 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, masterSeed: 42UL);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));

        // Verify every 4x4 block has uniform pixel colors due to pixelate(block_size = 4)
        for (var by = 0; by < 16; by += 4)
        {
            for (var bx = 0; bx < 16; bx += 4)
            {
                var corner = result.Bitmap!.GetPixel(bx, by);
                for (var dy = 0; dy < 4; dy++)
                {
                    for (var dx = 0; dx < 4; dx++)
                    {
                        result.Bitmap.GetPixel(bx + dx, by + dy).Should().Be(corner);
                    }
                }
            }
        }

        // Verify standalone color_adjust invert flips WHITE to BLACK
        const string invertSource = """
            layers = {
              bg = {
                type = fill
                color = rgb(200, 100, 50)
              }
              adj = {
                type = color_adjust
                invert = true
              }
            }
            output = {
              dimensions = { width = 4, height = 4 }
            }
            """;

        using var invResult = new GfxCanvasCompositor().RenderSource(invertSource);
        invResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, invResult.Diagnostics));
        invResult.Bitmap!.GetPixel(2, 2).Should().Be(new SKColor(55, 155, 205, 255));
    }

    [Fact]
    public void OutlineAndDropShadow_RenderStrokeAndOffsetShadowBehindShapeAndTextLayers()
    {
        const string shapeSource = """
            layers = {
              box = {
                type = rect
                size = { width = 4, height = 4 }
                offset = { x = 6, y = 6 }
                color = NAMED_COLORS.WHITE
              }
              outline_pass = {
                type = outline
                width = 2
                color = NAMED_COLORS.RED
              }
              shadow_pass = {
                type = drop_shadow
                offset = { x = 4, y = 4 }
                blur = 0
                color = NAMED_COLORS.BLUE
              }
            }
            output = {
              dimensions = { width = 20, height = 20 }
            }
            """;

        using var shapeResult = new GfxCanvasCompositor().RenderSource(shapeSource);
        shapeResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, shapeResult.Diagnostics));

        var bmp = shapeResult.Bitmap!;
        // Interior of box (6..9, 6..9) remains WHITE
        bmp.GetPixel(7, 7).Should().Be(new SKColor(255, 255, 255, 255));
        // 1px left of box at (5, 7) has the 2px RED outline
        bmp.GetPixel(5, 7).Should().Be(new SKColor(255, 0, 0, 255));
        // Shifted right/down at (14, 14) has the BLUE drop shadow behind the outlined box
        bmp.GetPixel(14, 14).Should().Be(new SKColor(0, 0, 255, 255));
        // Far corner (0, 0) remains transparent
        bmp.GetPixel(0, 0).Alpha.Should().Be(0);

        // Also test inline outline + drop_shadow on a text layer
        const string textSource = """
            layers = {
              label = {
                type = text
                text = "8"
                font_size = 32
                color = NAMED_COLORS.WHITE
                align = { horizontal = ALIGN.CENTER, vertical = ALIGN.CENTER }
                outline = { width = 2, color = NAMED_COLORS.RED }
                drop_shadow = { offset = { x = 3, y = 3 }, blur = 1.0, color = rgba(0, 0, 255, 200) }
              }
            }
            output = {
              dimensions = { width = 48, height = 48 }
            }
            """;

        using var textResult = new GfxCanvasCompositor().RenderSource(textSource);
        textResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, textResult.Diagnostics));

        var textPixels = Enumerable.Range(0, 48)
            .SelectMany(y => Enumerable.Range(0, 48).Select(x => textResult.Bitmap!.GetPixel(x, y)))
            .ToArray();

        // Contains white glyph interior, red outline pixels, and blue shadow pixels
        textPixels.Should().Contain(p => p.Red == 255 && p.Green == 255 && p.Blue == 255);
        textPixels.Should().Contain(p => p.Red > 200 && p.Green == 0 && p.Blue == 0);
        textPixels.Should().Contain(p => p.Blue > 50 && p.Red == 0 && p.Green == 0);
    }
}
