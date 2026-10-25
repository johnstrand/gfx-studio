using FluentAssertions;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests.Rendering;

public sealed class TextLayerRendererTests
{
    [Fact]
    public void RenderSource_UsesEmbeddedFontAndRendersNumericText()
    {
        const string numericSource = """
            layers = {
              label = {
                type = text
                text = 7
                font_size = 48
                align = { horizontal = ALIGN.CENTER, vertical = ALIGN.CENTER }
              }
            }
            output = { dimensions = { width = 64, height = 64 } }
            """;
        const string stringSource = """
            layers = {
              label = {
                type = text
                text = "7"
                font_size = 48
                align = { horizontal = ALIGN.CENTER, vertical = ALIGN.CENTER }
              }
            }
            output = { dimensions = { width = 64, height = 64 } }
            """;

        using var numeric = new GfxCanvasCompositor().RenderSource(numericSource, masterSeed: 42);
        using var textValue = new GfxCanvasCompositor().RenderSource(stringSource, masterSeed: 42);

        numeric.HasErrors.Should().BeFalse();
        numeric.Diagnostics.Should().BeEmpty();
        numeric.Bitmap.Should().NotBeNull();
        numeric.Bitmap!.GetPixelSpan().ToArray().Should().Equal(textValue.Bitmap!.GetPixelSpan().ToArray());
        var bounds = FindVisibleBounds(numeric.Bitmap);
        bounds.Width.Should().BeGreaterThan(0);
        bounds.Height.Should().BeGreaterThan(0);
        Math.Abs(((bounds.Left + bounds.Right) / 2.0f) - 31.5f).Should().BeLessThanOrEqualTo(1.0f);
        Math.Abs(((bounds.Top + bounds.Bottom) / 2.0f) - 31.5f).Should().BeLessThanOrEqualTo(1.0f);
    }

    [Fact]
    public void RenderSource_IsByteForByteDeterministicWithBundledFont()
    {
        const string source = """
            layers = {
              label = {
                type = text
                text = "GfxStudio 123"
                font_size = 24
                color = hex("#123456")
              }
            }
            output = { dimensions = { width = 96, height = 48 } }
            """;

        using var first = new GfxCanvasCompositor().RenderSource(source, masterSeed: 99);
        using var second = new GfxCanvasCompositor().RenderSource(source, masterSeed: 99);

        first.Diagnostics.Should().BeEmpty();
        first.Bitmap!.GetPixelSpan().ToArray().Should().Equal(second.Bitmap!.GetPixelSpan().ToArray());
    }

    [Fact]
    public void GfxFontManager_LoadsCustomFontRelativeToGfxFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "gfxstudio-font-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var embedded = typeof(GfxFontManager).Assembly.GetManifestResourceStream(GfxFontManager.FallbackFontResourceName))
            using (var destination = File.Create(Path.Combine(root, "custom.ttf")))
            {
                embedded.Should().NotBeNull();
                embedded!.CopyTo(destination);
            }

            using var typeface = new GfxFontManager().LoadTypeface("custom.ttf", null, Path.Combine(root, "sample.gfx"));
            typeface.FamilyName.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RenderSource_MissingTextReportsLayerDiagnostic()
    {
        const string source = """
            layers = {
              label = { type = text }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, filePath: "missing-text.gfx");

        result.HasErrors.Should().BeTrue();
        var diagnostic = result.Diagnostics.Should().ContainSingle(item => item.Code == "RND015").Subject;
        diagnostic.Span.FilePath.Should().Be("missing-text.gfx");
        diagnostic.Span.Start.Line.Should().Be(2);
        diagnostic.Message.Should().Contain("text");
    }

    [Fact]
    public void RenderSource_BoldWeightRendersVisiblyHeavierThanRegular()
    {
        const string regularSource = """
            layers = {
              label = {
                type = text
                text = "HelloWorld"
                font_size = 32
                font_weight = normal
                color = rgb(255, 255, 255)
              }
            }
            output = { dimensions = { width = 160, height = 48 } }
            """;

        const string boldSource = """
            layers = {
              label = {
                type = text
                text = "HelloWorld"
                font_size = 32
                font_weight = bold
                color = rgb(255, 255, 255)
              }
            }
            output = { dimensions = { width = 160, height = 48 } }
            """;

        using var regResult = new GfxCanvasCompositor().RenderSource(regularSource);
        using var boldResult = new GfxCanvasCompositor().RenderSource(boldSource);

        regResult.Diagnostics.Should().BeEmpty();
        boldResult.Diagnostics.Should().BeEmpty();

        var regBytes = regResult.Bitmap!.GetPixelSpan().ToArray();
        var boldBytes = boldResult.Bitmap!.GetPixelSpan().ToArray();
        regBytes.Should().NotEqual(boldBytes);

        var regOpaqueCount = CountNonTransparentPixels(regResult.Bitmap!);
        var boldOpaqueCount = CountNonTransparentPixels(boldResult.Bitmap!);
        boldOpaqueCount.Should().BeGreaterThan(regOpaqueCount);
    }

    [Fact]
    public void RenderSource_SupportsNamedAndNumericFontWeights()
    {
        const string source = """
            layers = {
              t1 = { type = text, text = "A", font_weight = thin }
              t2 = { type = text, text = "B", font_weight = "light" }
              t3 = { type = text, text = "C", font_weight = 600 }
              t4 = { type = text, text = "D", font_weight = 800 }
            }
            output = { dimensions = { width = 64, height = 64 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.Diagnostics.Should().BeEmpty();
        result.Bitmap.Should().NotBeNull();
    }

    [Fact]
    public void RenderSource_ItalicStyleRendersVisiblySlanted()
    {
        const string uprightSource = """
            layers = {
              label = {
                type = text
                text = "SlantTest"
                font_size = 28
                font_style = normal
                color = rgb(255, 255, 255)
              }
            }
            output = { dimensions = { width = 140, height = 40 } }
            """;

        const string italicSource = """
            layers = {
              label = {
                type = text
                text = "SlantTest"
                font_size = 28
                font_style = italic
                color = rgb(255, 255, 255)
              }
            }
            output = { dimensions = { width = 140, height = 40 } }
            """;

        using var upResult = new GfxCanvasCompositor().RenderSource(uprightSource);
        using var itResult = new GfxCanvasCompositor().RenderSource(italicSource);

        upResult.Diagnostics.Should().BeEmpty();
        itResult.Diagnostics.Should().BeEmpty();

        var upBytes = upResult.Bitmap!.GetPixelSpan().ToArray();
        var itBytes = itResult.Bitmap!.GetPixelSpan().ToArray();
        upBytes.Should().NotEqual(itBytes);
    }

    [Fact]
    public void RenderSource_InvalidFontWeight_ReportsRND023()
    {
        const string source = """
            layers = {
              label = {
                type = text
                text = "Hi"
                font_weight = super_heavy
              }
            }
            output = { dimensions = { width = 32, height = 32 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.Diagnostics.Should().Contain(d => d.Code == "RND023" && d.Message.Contains("Unknown font weight 'super_heavy'"));
    }

    [Fact]
    public void RenderSource_InvalidFontStyle_ReportsRND024()
    {
        const string source = """
            layers = {
              label = {
                type = text
                text = "Hi"
                font_style = cursive
              }
            }
            output = { dimensions = { width = 32, height = 32 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.Diagnostics.Should().Contain(d => d.Code == "RND024" && d.Message.Contains("Unknown font style 'cursive'"));
    }

    private static int CountNonTransparentPixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 10)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static SKRectI FindVisibleBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return new SKRectI(left, top, right, bottom);
    }
}
