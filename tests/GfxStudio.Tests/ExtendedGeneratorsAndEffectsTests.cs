using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests;

public sealed class ExtendedGeneratorsAndEffectsTests
{
    [Theory]
    [InlineData("running")]
    [InlineData("stack")]
    [InlineData("herringbone")]
    public void BricksLayer_RendersAllBondStylesDeterministically(string bond)
    {
        var source = $$"""
            output = { dimensions = { width = 48, height = 48 } }
            layers = {
              wall = {
                type = bricks
                bond = {{bond}}
                brick_width = 16
                brick_height = 8
                mortar = 2
                bevel = 1.5
                variation = 0.25
                color = rgb(179, 71, 42)
                mortar_color = rgb(216, 210, 199)
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var r1 = compositor.RenderSource(source, masterSeed: 42);
        using var r2 = compositor.RenderSource(source, masterSeed: 42);

        Assert.False(r1.HasErrors, string.Join("; ", r1.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(r1.Bitmap);
        Assert.NotNull(r2.Bitmap);
        AssertBitmapsEqual(r1.Bitmap!, r2.Bitmap!);
        AssertHasMultipleDistinctColors(r1.Bitmap!, minDistinct: 3);
    }

    [Theory]
    [InlineData("arcs")]
    [InlineData("diagonal")]
    [InlineData("triangles")]
    public void TruchetLayer_RendersAllStylesDeterministically(string style)
    {
        var source = $$"""
            output = { dimensions = { width = 48, height = 48 } }
            layers = {
              maze = {
                type = truchet
                style = {{style}}
                cell_size = 12
                thickness = 2.5
                color_a = rgb(244, 241, 222)
                color_b = rgb(29, 53, 87)
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var r1 = compositor.RenderSource(source, masterSeed: 99);
        using var r2 = compositor.RenderSource(source, masterSeed: 99);

        Assert.False(r1.HasErrors, string.Join("; ", r1.Diagnostics.Select(d => d.Message)));
        AssertBitmapsEqual(r1.Bitmap!, r2.Bitmap!);
        AssertHasMultipleDistinctColors(r1.Bitmap!, minDistinct: 2);
    }

    [Theory]
    [InlineData("tiling", "hex")]
    [InlineData("hex_grid", "hex")]
    [InlineData("tiling", "triangle")]
    [InlineData("tiling", "isometric")]
    [InlineData("chevron", "chevron")]
    public void GeometricTilingLayer_RendersAllStyles(string layerType, string style)
    {
        var source = $$"""
            output = { dimensions = { width = 48, height = 48 } }
            layers = {
              grid = {
                type = {{layerType}}
                style = {{style}}
                cell_size = 14
                line_width = 1.5
                variation = 0.3
                color = color_ramp(rgb(38, 70, 83), rgb(42, 157, 143), rgb(233, 196, 106))
                line_color = rgb(17, 22, 29)
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source, masterSeed: 7);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Bitmap);
        AssertHasMultipleDistinctColors(result.Bitmap!, minDistinct: 3);
    }

    [Theory]
    [InlineData("sine")]
    [InlineData("radial")]
    [InlineData("interference")]
    [InlineData("plasma")]
    [InlineData("square")]
    [InlineData("sawtooth")]
    [InlineData("triangle")]
    public void WavesLayer_RendersAllWaveforms(string style)
    {
        var source = $$"""
            output = { dimensions = { width = 40, height = 40 } }
            layers = {
              w = {
                type = waves
                style = {{style}}
                frequency = 4
                angle = 30
                color = color_ramp(rgb(11, 19, 43), rgb(58, 80, 107), rgb(91, 192, 190))
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        AssertHasMultipleDistinctColors(result.Bitmap!, minDistinct: style == "square" ? 2 : 3);
    }

    [Theory]
    [InlineData("archimedean")]
    [InlineData("logarithmic")]
    [InlineData("vortex")]
    public void SpiralLayer_RendersAllStyles(string style)
    {
        var source = $$"""
            output = { dimensions = { width = 40, height = 40 } }
            layers = {
              sp = {
                type = spiral
                style = {{style}}
                arms = 3
                tightness = 3.5
                color = color_ramp(rgb(16, 0, 43), rgb(123, 44, 191), rgb(224, 170, 255))
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        AssertHasMultipleDistinctColors(result.Bitmap!, minDistinct: 4);
    }

    [Theory]
    [InlineData("wood")]
    [InlineData("marble")]
    [InlineData("caustics")]
    [InlineData("flow_field")]
    [InlineData("curl_noise")]
    [InlineData("starfield")]
    public void OrganicTextureLayers_RenderDeterministically(string layerType)
    {
        var source = $$"""
            output = { dimensions = { width = 48, height = 48 } }
            layers = {
              tex = {
                type = {{layerType}}
                scale = 4.0
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var r1 = compositor.RenderSource(source, masterSeed: 1234);
        using var r2 = compositor.RenderSource(source, masterSeed: 1234);

        Assert.False(r1.HasErrors, string.Join("; ", r1.Diagnostics.Select(d => d.Message)));
        AssertBitmapsEqual(r1.Bitmap!, r2.Bitmap!);
        AssertHasMultipleDistinctColors(r1.Bitmap!, minDistinct: 4);
    }

    [Theory]
    [InlineData("coral")]
    [InlineData("spots")]
    [InlineData("fingerprints")]
    [InlineData("mitosis")]
    public void ReactionDiffusionLayer_RendersPresetsDeterministically(string preset)
    {
        var source = $$"""
            output = { dimensions = { width = 36, height = 36 } }
            layers = {
              rd = {
                type = reaction_diffusion
                preset = {{preset}}
                iterations = 45
                color = color_ramp(rgb(15, 23, 42), rgb(56, 189, 248), rgb(248, 250, 252))
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var r1 = compositor.RenderSource(source, masterSeed: 77);
        using var r2 = compositor.RenderSource(source, masterSeed: 77);

        Assert.False(r1.HasErrors, string.Join("; ", r1.Diagnostics.Select(d => d.Message)));
        AssertBitmapsEqual(r1.Bitmap!, r2.Bitmap!);
        AssertHasMultipleDistinctColors(r1.Bitmap!, minDistinct: 4);
    }

    [Fact]
    public void SdfLayer_RendersCsgPrimitivesGlowStrokeAndIsolines()
    {
        var source = """
            output = { dimensions = { width = 64, height = 64 } }
            layers = {
              field = {
                type = sdf
                mode = isolines
                isoline_spacing = 8
                color = color_ramp(rgb(13, 27, 42), rgb(27, 38, 59), rgb(224, 225, 221))
                fill_color = rgb(255, 107, 107)
                stroke_color = rgb(255, 255, 255)
                stroke_width = 1.5
                glow = 4
                glow_color = rgb(255, 209, 102)
                shapes = [
                  { shape = circle, center = { x = 0.38, y = 0.5 }, radius = 0.24, op = union },
                  { shape = box, center = { x = 0.62, y = 0.5 }, size = { x = 0.36, y = 0.32 }, corner_radius = 0.05, op = smooth_union, smoothness = 0.08 },
                  { shape = circle, center = { x = 0.5, y = 0.5 }, radius = 0.1, op = smooth_subtract, smoothness = 0.03 }
                ]
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        AssertHasMultipleDistinctColors(result.Bitmap!, minDistinct: 8);
    }

    [Fact]
    public void SpatialDistortionEffects_ApplyFbmWarpCrossLayerDisplaceTwirlRippleSpherizeAndKaleidoscope()
    {
        var source = """
            output = { dimensions = { width = 48, height = 48 } }
            layers = {
              disp_map = {
                type = fractal_clouds
                scale = 3.0
                visible = true
              }
              base = {
                type = checkerboard
                size = { width = 8, height = 8 }
                color_a = rgb(255, 89, 94)
                color_b = rgb(25, 130, 196)
                warp = { map = disp_map, strength = 6 }
                twirl = { angle = 60, radius = 20 }
                ripple = { amplitude = 3, wavelength = 12, mode = radial }
                spherize = { amount = 0.4, radius = 22 }
              }
              kaleido = {
                type = kaleidoscope
                segments = 6
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source, masterSeed: 55);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        AssertHasMultipleDistinctColors(result.Bitmap!, minDistinct: 5);
    }

    [Theory]
    [InlineData("emboss", "{ angle = 135, strength = 1.5, blend_original = true }")]
    [InlineData("bevel", "{ depth = 4, angle = 120, soften = 1 }")]
    [InlineData("normal_map", "{ strength = 3.0 }")]
    [InlineData("halftone", "{ dot_size = 5, mode = mono }")]
    [InlineData("halftone", "{ dot_size = 6, mode = cmyk }")]
    [InlineData("dither", "{ style = bayer, levels = 2, scale = 1 }")]
    [InlineData("dither", "{ style = floyd_steinberg, levels = 2 }")]
    [InlineData("dither", "{ style = atkinson, levels = 2 }")]
    [InlineData("stipple", "{ levels = 2, color_a = rgb(26, 26, 26), color_b = rgb(245, 240, 230) }")]
    [InlineData("edge_detect", "{ mode = neon, threshold = 0.1 }")]
    [InlineData("vignette", "{ strength = 0.85, radius = 0.7 }")]
    [InlineData("bloom", "{ threshold = 0.5, radius = 4, intensity = 0.9 }")]
    [InlineData("chromatic_aberration", "{ offset = { x = 3, y = 1 }, radial = true }")]
    [InlineData("crt", "{ scanline_opacity = 0.35, curvature = 0.12 }")]
    [InlineData("oil_paint", "{ radius = 2 }")]
    public void SurfaceAndStylizationEffects_ExecuteWithoutErrors(string effectType, string propsBlock)
    {
        var source = $$"""
            output = { dimensions = { width = 36, height = 36 } }
            layers = {
              bg = {
                type = gradient
                style = radial
                color = color_ramp(rgb(255, 255, 255), rgb(255, 107, 107), rgb(29, 53, 87))
              }
              shape = {
                type = star
                sides = 5
                fill_color = rgb(255, 209, 102)
                stroke_color = rgb(7, 59, 76)
                stroke_width = 2
              }
              fx = {
                type = {{effectType}}
                {{propsBlock.Trim('{', '}', ' ')}}
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source, masterSeed: 101);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Bitmap);
        AssertHasMultipleDistinctColors(result.Bitmap!, minDistinct: 2);
    }

    [Fact]
    public void ProceduralShowcaseSample_RendersCleanly()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var samplePath = Path.Combine(repoRoot, "samples", "procedural_showcase.gfx");
        var source = File.ReadAllText(samplePath);

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source, masterSeed: 2026, filePath: samplePath);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Bitmap);
        Assert.Equal(256, result.Bitmap!.Width);
        Assert.Equal(256, result.Bitmap!.Height);
        AssertHasMultipleDistinctColors(result.Bitmap!, minDistinct: 32);
    }

    private static void AssertBitmapsEqual(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected.GetPixel(x, y), actual.GetPixel(x, y));
            }
        }
    }

    private static void AssertHasMultipleDistinctColors(SKBitmap bitmap, int minDistinct)
    {
        var colors = new HashSet<SKColor>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                colors.Add(bitmap.GetPixel(x, y));
                if (colors.Count >= minDistinct)
                {
                    return;
                }
            }
        }

        Assert.True(colors.Count >= minDistinct, $"Expected at least {minDistinct} distinct colors, but found {colors.Count}.");
    }
}
