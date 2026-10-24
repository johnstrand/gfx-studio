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
