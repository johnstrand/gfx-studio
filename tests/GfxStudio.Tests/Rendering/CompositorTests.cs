using FluentAssertions;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests.Rendering;

public sealed class CompositorTests
{
    [Fact]
    public void RenderSource_CompositesLayersInDeclarationOrderAndSupportsMultiplyBlendMode()
    {
        const string source = """
            layers = {
              layer0 = {
                type = test_solid
                color = rgb(0, 255, 255)
              }
              layer1 = {
                type = test_solid
                color = rgb(128, 128, 128)
                mix = multiply
                size = output.dimensions
              }
            }

            output = {
              dimensions = { width = 16, height = 16 }
            }
            """;

        var renderOrders = new List<string>();
        var registry = LayerRendererRegistry.CreateDefault([new TestSolidLayerRenderer(renderOrders)]);
        var compositor = new GfxCanvasCompositor(registry);

        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();
        result.Bitmap.Should().NotBeNull();

        renderOrders.Should().Equal("layer0", "layer1");

        // Acceptance Criterion 2: (128, 128, 128, 255) multiplied over (0, 255, 255, 255) -> (0, 128, 128, 255)
        var pixel = result.Bitmap!.GetPixel(8, 8);
        pixel.Should().Be(new SKColor(0, 128, 128, 255));
    }

    [Fact]
    public void RenderSource_RespectsOffsetSizeOpacityAndVisibility()
    {
        const string source = """
            layers = {
              bg = {
                type = test_solid
                color = rgb(0, 0, 0)
              }
              box = {
                type = test_solid
                color = rgb(200, 100, 0)
                size = { width = 4, height = 4 }
                offset = { x = 2, y = 3 }
                opacity = 0.5
              }
              hidden = {
                type = test_solid
                color = rgb(255, 255, 255)
                visible = false
              }
            }

            output = {
              dimensions = { width = 10, height = 10 }
            }
            """;

        var renderedLayers = new List<string>();
        var registry = LayerRendererRegistry.CreateDefault([new TestSolidLayerRenderer(renderedLayers)]);
        var compositor = new GfxCanvasCompositor(registry);

        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeFalse();
        renderedLayers.Should().Equal("bg", "box"); // 'hidden' was skipped

        // Outside the 4x4 box (at 0,0) -> untouched black background (0, 0, 0, 255)
        result.Bitmap!.GetPixel(0, 0).Should().Be(new SKColor(0, 0, 0, 255));

        // Inside the 4x4 box at offset (2,3)..(5,6) with 0.5 opacity over (0,0,0) -> (100, 50, 0, 255)
        result.Bitmap!.GetPixel(2, 3).Should().Be(new SKColor(100, 50, 0, 255));
        result.Bitmap!.GetPixel(5, 6).Should().Be(new SKColor(100, 50, 0, 255));
        result.Bitmap!.GetPixel(6, 7).Should().Be(new SKColor(0, 0, 0, 255));
    }

    [Fact]
    public void RenderSource_UnknownLayerType_ReportsDiagnosticListingAvailableTypes()
    {
        const string source = """
            layers = {
              layer0 = {
                type = unknown_layer
              }
            }

            output = {
              dimensions = { width = 8, height = 8 }
            }
            """;

        var registry = LayerRendererRegistry.CreateDefault([
            new TestSolidLayerRenderer(),
            new DummyNamedRenderer("fill"),
            new DummyNamedRenderer("text")
        ]);
        var compositor = new GfxCanvasCompositor(registry);

        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeTrue();
        var diag = result.Diagnostics.Should().ContainSingle(d => d.Code == "RND004").Subject;
        diag.Message.Should().Contain("unknown_layer");
        diag.Message.Should().Contain("fill").And.Contain("test_solid").And.Contain("text");
    }

    [Fact]
    public void PixelBlender_SupportsAllNineBlendModesDeterministically()
    {
        var backdrop = new ColorRgba(100, 200, 50, 255);
        var source = new ColorRgba(50, 100, 150, 255);

        PixelBlender.BlendPixel(backdrop, source, BlendMode.Normal)
            .Should().Be(new ColorRgba(50, 100, 150, 255));

        PixelBlender.BlendPixel(new ColorRgba(0, 255, 255, 255), new ColorRgba(128, 128, 128, 255), BlendMode.Multiply)
            .Should().Be(new ColorRgba(0, 128, 128, 255));

        PixelBlender.BlendPixel(backdrop, source, BlendMode.Add)
            .Should().Be(new ColorRgba(150, 255, 200, 255));

        PixelBlender.BlendPixel(backdrop, source, BlendMode.Subtract)
            .Should().Be(new ColorRgba(50, 100, 0, 255));

        PixelBlender.BlendPixel(backdrop, source, BlendMode.Darken)
            .Should().Be(new ColorRgba(50, 100, 50, 255));

        PixelBlender.BlendPixel(backdrop, source, BlendMode.Lighten)
            .Should().Be(new ColorRgba(100, 200, 150, 255));

        PixelBlender.BlendPixel(backdrop, source, BlendMode.Difference)
            .Should().Be(new ColorRgba(50, 100, 100, 255));

        // Screen: 1 - (1 - d) * (1 - s)
        PixelBlender.BlendPixel(new ColorRgba(128, 0, 255, 255), new ColorRgba(128, 128, 0, 255), BlendMode.Screen)
            .Should().Be(new ColorRgba(192, 128, 255, 255));

        // Overlay: d < 0.5 -> 2*d*s; d >= 0.5 -> 1 - 2*(1-d)*(1-s)
        var overlayResult = PixelBlender.BlendPixel(new ColorRgba(64, 192, 128, 255), new ColorRgba(128, 128, 128, 255), BlendMode.Overlay);
        overlayResult.R.Should().BeInRange(63, 65);
        overlayResult.G.Should().BeInRange(191, 193);
    }

    private sealed class TestSolidLayerRenderer(List<string>? orderTracker = null) : ILayerRenderer
    {
        public string LayerType => "test_solid";

        public IReadOnlyCollection<string> SupportedProperties => ["color"];

        public SKBitmap RenderLayer(LayerRenderContext context)
        {
            orderTracker?.Add(context.LayerId);
            var bitmap = context.CreateLayerBitmap();
            if (context.LayerProperties.TryGetProperty("color", out var colorVal) && colorVal is GfxColor gfxColor)
            {
                bitmap.Erase(gfxColor.Color.ToSKColor());
            }

            return bitmap;
        }
    }

    private sealed class DummyNamedRenderer(string layerType) : ILayerRenderer
    {
        public string LayerType { get; } = layerType;

        public SKBitmap RenderLayer(LayerRenderContext context) => context.CreateLayerBitmap();
    }
}
