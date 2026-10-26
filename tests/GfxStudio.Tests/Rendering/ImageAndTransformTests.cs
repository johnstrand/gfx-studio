using FluentAssertions;
using GfxStudio.Core.Output;
using GfxStudio.Core.Rendering;
using GfxStudio.Core.Rendering.Layers;
using SkiaSharp;

namespace GfxStudio.Tests.Rendering;

public sealed class ImageAndTransformTests
{
    [Fact]
    public void ImageLayer_LoadsRelativeToGfxDirectory_SupportsCropTileAndNearestResample()
    {
        var tempDir = CreateTemporaryDirectory();
        try
        {
            var assetsDir = Path.Combine(tempDir, "assets");
            Directory.CreateDirectory(assetsDir);
            var spritePath = Path.Combine(assetsDir, "tiny.png");

            // Create a 2x2 pixel-art image:
            // (0,0)=RED, (1,0)=GREEN
            // (0,1)=BLUE, (1,1)=WHITE
            using (var bmp = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul))
            {
                bmp.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
                bmp.SetPixel(1, 0, new SKColor(0, 255, 0, 255));
                bmp.SetPixel(0, 1, new SKColor(0, 0, 255, 255));
                bmp.SetPixel(1, 1, new SKColor(255, 255, 255, 255));
                new SingleImageOutputWriter().Write(bmp, spritePath);
            }

            // 1. Test nearest-neighbor stretch scaling of 2x2 to 8x8 -> crisp 4x4 blocks
            var stretchGfxPath = Path.Combine(tempDir, "stretch.gfx");
            File.WriteAllText(stretchGfxPath, """
                layers = {
                  img = {
                    type = image
                    source = "assets/tiny.png"
                    fit = stretch
                    resample = nearest
                  }
                }
                output = {
                  dimensions = { width = 8, height = 8 }
                  filename = "stretch_out.png"
                }
                """);

            using var stretchResult = new GfxEngine().RenderFile(stretchGfxPath);
            stretchResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, stretchResult.Diagnostics));
            var stretched = stretchResult.Artifacts.Single().Bitmap;
            stretched.GetPixel(3, 3).Should().Be(new SKColor(255, 0, 0, 255));   // still pure RED at boundary
            stretched.GetPixel(4, 3).Should().Be(new SKColor(0, 255, 0, 255));   // crisp step to pure GREEN
            stretched.GetPixel(3, 4).Should().Be(new SKColor(0, 0, 255, 255));   // pure BLUE
            stretched.GetPixel(4, 4).Should().Be(new SKColor(255, 255, 255, 255)); // pure WHITE

            // 2. Test crop + tile mode
            var tileGfxPath = Path.Combine(tempDir, "tile.gfx");
            File.WriteAllText(tileGfxPath, """
                layers = {
                  img = {
                    type = image
                    source = "assets/tiny.png"
                    crop = { x = 0, y = 0, width = 2, height = 1 }
                    fit = tile
                  }
                }
                output = {
                  dimensions = { width = 6, height = 4 }
                  filename = "tile_out.png"
                }
                """);

            using var tileResult = new GfxEngine().RenderFile(tileGfxPath);
            tileResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, tileResult.Diagnostics));
            var tiled = tileResult.Artifacts.Single().Bitmap;
            for (var y = 0; y < 4; y++)
            {
                tiled.GetPixel(0, y).Should().Be(new SKColor(255, 0, 0, 255));
                tiled.GetPixel(1, y).Should().Be(new SKColor(0, 255, 0, 255));
                tiled.GetPixel(2, y).Should().Be(new SKColor(255, 0, 0, 255));
                tiled.GetPixel(3, y).Should().Be(new SKColor(0, 255, 0, 255));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LayerTransformsAndMasking_SupportFlipRotateScaleAndLayerMask()
    {
        const string source = """
            layers = {
              bg = {
                type = fill
                color = NAMED_COLORS.BLACK
              }
              stencil = {
                type = rect
                size = { width = 8, height = 8 }
                offset = { x = 4, y = 4 }
                color = NAMED_COLORS.WHITE
                visible = false
              }
              masked_red = {
                type = fill
                color = NAMED_COLORS.RED
                mask = stencil
              }
              asym_box = {
                type = stripes
                stripe_width = 8
                angle = 0
                color1 = NAMED_COLORS.TRANSPARENT
                color2 = NAMED_COLORS.GREEN
                flip_x = true
              }
            }
            output = {
              dimensions = { width = 16, height = 16 }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));

        var bmp = result.Bitmap!;
        // Inside the stencil (4..11, 4..11) -> RED
        bmp.GetPixel(8, 8).Should().Be(new SKColor(255, 0, 0, 255));
        // Flipped stripes layer (originally x = 0..7 TRANSPARENT, x = 8..15 GREEN; flipped horizontally -> x = 0..7 GREEN, x = 8..15 TRANSPARENT)
        bmp.GetPixel(2, 8).Should().Be(new SKColor(0, 255, 0, 255));
        bmp.GetPixel(14, 8).Should().Be(new SKColor(0, 0, 0, 255));
    }

    [Theory]
    [InlineData("wrap_mode", "repeat", 0, 1, 0, 1)]
    [InlineData("wrap", "repeat", 0, 1, 0, 1)]
    [InlineData("wrap_mode", "mirror", 0, 1, 1, 0)]
    [InlineData("wrap_mode", "clamp", 0, 1, 1, 1)]
    public void LayerTransforms_WrapScaledLayersUsingConfiguredMode(
        string propertyName,
        string wrapMode,
        byte expected0,
        byte expected1,
        byte expected2,
        byte expected3)
    {
        var source = $$"""
            layers = {
              stripes = {
                type = stripes
                stripe_width = 2
                angle = 0
                color1 = NAMED_COLORS.RED
                color2 = NAMED_COLORS.GREEN
                scale = 0.5
                anchor = { x = 0, y = 0 }
                resample = nearest
                {{propertyName}} = {{wrapMode}}
              }
            }
            output = { dimensions = { width = 4, height = 1 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, masterSeed: 1);

        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
        var expectedColors = new[] { expected0, expected1, expected2, expected3 }
            .Select(isGreen => isGreen == 0
                ? new SKColor(255, 0, 0, 255)
                : new SKColor(0, 255, 0, 255));
        for (var x = 0; x < 4; x++)
        {
            result.Bitmap!.GetPixel(x, 0).Should().Be(expectedColors.ElementAt(x));
        }
    }

    [Fact]
    public void LayerTransforms_InvalidWrapMode_ReportsDiagnostic()
    {
        const string source = """
            layers = {
              stripes = {
                type = stripes
                wrap_mode = diagonal
              }
            }
            output = { dimensions = { width = 4, height = 4 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, masterSeed: 1);

        result.Diagnostics.Should().ContainSingle(d => d.Code == "RND036" && d.Message.Contains("wrap_mode"));
    }

    [Fact]
    public void LayerTransforms_PreserveBooleanWrapOnSpatialEffectLayers()
    {
        const string source = """
            layers = {
              background = { type = fill, color = NAMED_COLORS.WHITE }
              distortion = { type = warp, wrap = true, strength = 0.5 }
            }
            output = { dimensions = { width = 4, height = 4 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, masterSeed: 1);

        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ImageLayer_ReusesCachedDecodedImageAcrossBatchRuns()
    {
        var tempDir = CreateTemporaryDirectory();
        try
        {
            var imgPath = Path.Combine(tempDir, "cached.png");
            using (var bmp = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul))
            {
                bmp.Erase(new SKColor(20, 140, 220, 255));
                new SingleImageOutputWriter().Write(bmp, imgPath);
            }

            var cache = new GfxImageCache();
            var registry = LayerRendererRegistry.CreateDefault([new ImageLayerRenderer(cache)]);
            var engine = new GfxEngine(new GfxCanvasCompositor(registry));

            var gfxPath = Path.Combine(tempDir, "batch.gfx");
            File.WriteAllText(gfxPath, """
                layers = {
                  img = {
                    type = image
                    source = "cached.png"
                  }
                }
                output = {
                  mode = series
                  count = 5
                  dimensions = { width = 4, height = 4 }
                  filename = "out_${run.number:0000}.png"
                }
                """);

            using var result = engine.RenderFile(gfxPath);
            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().HaveCount(5);

            cache.MissCount.Should().Be(1);
            cache.HitCount.Should().Be(4);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TransformBlock_SupportsOffsetRotationScaleShearFlipAnchorWrapAndResample()
    {
        const string source = """
            layers = {
              bg = { type = fill, color = NAMED_COLORS.BLACK }
              box = {
                type = rect
                size = { width = 4, height = 4 }
                color = NAMED_COLORS.RED
                transform = {
                  offset = { x = 2, y = 3 }
                  scale = { x = 2.0, y = 1.0 }
                  anchor = { x = 0, y = 0 }
                  resample = nearest
                }
              }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));

        var bmp = result.Bitmap!;
        bmp.GetPixel(1, 3).Should().Be(new SKColor(0, 0, 0, 255));
        bmp.GetPixel(2, 3).Should().Be(new SKColor(255, 0, 0, 255));
        bmp.GetPixel(3, 5).Should().Be(new SKColor(255, 0, 0, 255));
    }

    [Fact]
    public void TransformBlock_DisambiguatesTransformScaleOnNoiseLayerAndTransformOffsetOnEffectLayer()
    {
        const string source = """
            layers = {
              clouds = {
                type = fractal_clouds
                scale = 0.08
                transform = {
                  scale = 2.0
                  rotation = 45
                }
              }
              shadow = {
                type = drop_shadow
                offset = { x = 2, y = 2 }
                radius = 2
                transform = {
                  offset = { x = 0, y = 0 }
                }
              }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, masterSeed: 42);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
        result.Diagnostics.Should().BeEmpty();
        result.Bitmap.Should().NotBeNull();
    }

    [Fact]
    public void TransformBlock_ReportsRND050WithDidYouMeanSuggestionOnUnknownProperty()
    {
        const string source = """
            layers = {
              box = {
                type = rect
                size = { width = 8, height = 8 }
                color = NAMED_COLORS.RED
                transform = {
                  rotaton = 45
                }
              }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == "RND050" &&
            d.Message.Contains("rotaton") &&
            d.Message.Contains("rotation"));
    }

    [Fact]
    public void LayerMetadataCatalog_ProvidesPropertyDescriptorsAndCategoryForAllRegisteredLayersAndEffects()
    {
        var layerRegistry = LayerRendererRegistry.CreateDefault();
        foreach (var layerType in layerRegistry.RegisteredLayerTypes)
        {
            layerRegistry.TryGet(layerType, out var renderer).Should().BeTrue();
            renderer!.Category.Should().NotBeNullOrWhiteSpace($"layer '{layerType}' must have a category");
            renderer.Description.Should().NotBeNullOrWhiteSpace($"layer '{layerType}' must have a description");
            renderer.ExampleGfx.Should().NotBeNullOrWhiteSpace($"layer '{layerType}' must have an example");
            if (layerType != "empty")
            {
                renderer.PropertyDescriptors.Should().NotBeEmpty($"layer '{layerType}' must expose property descriptors");
            }
        }

        GfxCanvasCompositor.UniversalPropertyDescriptors.Should().NotBeEmpty();
        GfxCanvasCompositor.TransformPropertyDescriptors.Should().NotBeEmpty();
    }

    [Fact]
    public void PatternOffset_ShiftsPeriodicPatternsByFullPeriodIdenticallyToZeroOffset()
    {
        const string zeroOffsetSource = """
            layers = {
              s = {
                type = stripes
                stripe_width = 4
                angle = 0
                color1 = NAMED_COLORS.RED
                color2 = NAMED_COLORS.BLUE
                pattern_offset = { x = 0, y = 0 }
              }
              c = {
                type = checkerboard
                cell_size = 4
                color1 = NAMED_COLORS.WHITE
                color2 = NAMED_COLORS.BLACK
                pattern_offset = { x = 0, y = 0 }
                opacity = 0.5
              }
              g = {
                type = grid
                spacing = 4
                line_width = 1
                pattern_offset = { x = 0, y = 0 }
                opacity = 0.5
              }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        const string fullPeriodOffsetSource = """
            layers = {
              s = {
                type = stripes
                stripe_width = 4
                angle = 0
                color1 = NAMED_COLORS.RED
                color2 = NAMED_COLORS.BLUE
                pattern_offset = { x = 8, y = 0 }
              }
              c = {
                type = checkerboard
                cell_size = 4
                color1 = NAMED_COLORS.WHITE
                color2 = NAMED_COLORS.BLACK
                phase_offset = { x = -8, y = 8 }
                opacity = 0.5
              }
              g = {
                type = grid
                spacing = 4
                line_width = 1
                pattern_offset = { x = -4, y = 4 }
                opacity = 0.5
              }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        var compositor = new GfxCanvasCompositor();
        using var r0 = compositor.RenderSource(zeroOffsetSource, masterSeed: 7);
        using var r1 = compositor.RenderSource(fullPeriodOffsetSource, masterSeed: 7);

        r0.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, r0.Diagnostics));
        r1.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, r1.Diagnostics));

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                r1.Bitmap!.GetPixel(x, y).Should().Be(r0.Bitmap!.GetPixel(x, y), $"pixel ({x},{y}) should match after full-period shift");
            }
        }
    }

    [Fact]
    public void PatternOffset_WorksAcrossAllProceduralAndPatternLayerTypesWithoutDiagnostics()
    {
        const string source = """
            layers = {
              l1 = { type = stripes, stripe_width = 4, phase = 2 }
              l2 = { type = dots, spacing = 8, pattern_offset = { x = 2, y = -3 } }
              l3 = { type = bricks, brick_width = 8, brick_height = 4, pattern_offset = { x = -4, y = 2 } }
              l4 = { type = truchet, cell_size = 8, phase_offset = { x = 4, y = 4 } }
              l5 = { type = tiling, cell_size = 8, pattern_offset = { x = 3, y = 5 } }
              l6 = { type = waves, wavelength = 8, pattern_offset = { x = 2, y = 1 } }
              l7 = { type = fractal_clouds, scale = 0.1, pattern_offset = { x = 10, y = -10 } }
              l8 = { type = worley, cell_size = 8, phase_offset = { x = 5, y = 5 } }
              l9 = { type = wood, rings = 6, pattern_offset = { x = 4, y = 4 } }
              l10 = { type = marble, frequency = 0.1, pattern_offset = { x = -6, y = 3 } }
              l11 = { type = caustics, scale = 4, pattern_offset = { x = 4, y = -4 } }
              l12 = { type = flow_field, scale = 0.05, pattern_offset = { x = 8, y = 8 } }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source, masterSeed: 123);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
        result.Diagnostics.Should().BeEmpty();
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gfxstudio-image-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
