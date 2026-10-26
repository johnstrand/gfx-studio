using FluentAssertions;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests;

public sealed class PixelShaderTests
{
    [Fact]
    public void PixelShader_SupportsArrayRgbReturnAndClampsToUnitRange()
    {
        var source = """
            output = { dimensions = { width = 4, height = 4 } }
            layers = {
              sh = {
                type = shader
                fn = "(u, v) => [-0.4, 0.5, 1.7]"
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var px = result.Bitmap!.GetPixel(2, 2);
        Assert.Equal(0, px.Red);
        Assert.InRange(px.Green, 127, 128);
        Assert.Equal(255, px.Blue);
        Assert.Equal(255, px.Alpha);
    }

    [Fact]
    public void PixelShader_SupportsArrayRgbaAndObjectRgbAndObjectRgbaReturns()
    {
        var source = """
            output = { dimensions = { width = 16, height = 16 } }
            layers = {
              q1 = {
                type = shader
                fn = "(u, v) => u < 0.5 ? [1.0, 0.0, 0.0, 1.0] : (v < 0.5 ? { r: 0.0, g: 1.0, b: 0.0 } : { r: 0.0, g: 0.0, b: 1.0, a: 1.0 })"
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(new SKColor(255, 0, 0, 255), result.Bitmap!.GetPixel(2, 2));
        Assert.Equal(new SKColor(0, 255, 0, 255), result.Bitmap!.GetPixel(12, 2));
        Assert.Equal(new SKColor(0, 0, 255, 255), result.Bitmap!.GetPixel(12, 12));
    }

    [Fact]
    public void PixelShader_ScalarReturnMapsThroughColorRamp()
    {
        var source = """
            output = { dimensions = { width = 16, height = 8 } }
            layers = {
              ramp_shader = {
                type = pixel_shader
                color = color_ramp(rgb(255, 0, 0), rgb(0, 0, 255))
                fn = "(u, v) => u"
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(new SKColor(255, 0, 0, 255), result.Bitmap!.GetPixel(0, 4));
        Assert.Equal(new SKColor(0, 0, 255, 255), result.Bitmap!.GetPixel(15, 4));
    }

    [Fact]
    public void PixelShader_ExecutesExportedJsModuleWithNoiseParamsAndCrossLayerSampling()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gfx_shader_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var jsPath = Path.Combine(tempDir, "shaders.js");
            File.WriteAllText(jsPath, """
                export function plasmaOrb(u, v, ctx, x, y) {
                    const dx = u - 0.5;
                    const dy = v - 0.5;
                    const d = length(dx, dy);
                    if (d > ctx.params.radius) {
                        return [0, 0, 0, 0];
                    }
                    const n = ctx.fbm(u * 4.0, v * 4.0, ctx.t);
                    const maskSample = ctx.sampleLayer("pattern_ref", u, v);
                    const r = clamp(mix(ctx.params.tint.r, maskSample.r, 0.35) + n * 0.25, 0, 1);
                    const g = clamp(ctx.params.tint.g * (1.0 - d) + n * 0.2, 0, 1);
                    const b = clamp(ctx.params.tint.b + maskSample.b * 0.4, 0, 1);
                    const a = smoothstep(ctx.params.radius, ctx.params.radius - 0.05, d);
                    return { r, g, b, a };
                }
                """);

            var gfxPath = Path.Combine(tempDir, "scene.gfx");
            var gfxSource = """
                scripts = {
                  sh = "shaders.js"
                }
                output = { dimensions = { width = 32, height = 32 } }
                layers = {
                  bg = {
                    type = fill
                    color = rgb(10, 20, 40)
                  }
                  pattern_ref = {
                    type = checkerboard
                    size = { width = 8, height = 8 }
                    color_a = rgb(255, 200, 50)
                    color_b = rgb(40, 120, 255)
                    visible = false
                  }
                  orb = {
                    type = shader
                    fn = sh.plasmaOrb
                    params = {
                      radius = 0.42
                      tint = rgb(240, 80, 160)
                    }
                  }
                }
                """;
            File.WriteAllText(gfxPath, gfxSource);

            var compositor = new GfxCanvasCompositor();
            using var r1 = compositor.RenderSource(gfxSource, masterSeed: 99, filePath: gfxPath);
            using var r2 = compositor.RenderSource(gfxSource, masterSeed: 99, filePath: gfxPath);

            Assert.False(r1.HasErrors, string.Join("; ", r1.Diagnostics.Select(d => d.Message)));
            // Corner pixel (outside orb radius) should preserve background color rgb(10, 20, 40)
            Assert.Equal(new SKColor(10, 20, 40, 255), r1.Bitmap!.GetPixel(0, 0));
            // Center pixel (inside orb) should be modified and deterministic
            Assert.NotEqual(new SKColor(10, 20, 40, 255), r1.Bitmap!.GetPixel(16, 16));
            Assert.Equal(r1.Bitmap!.GetPixel(16, 16), r2.Bitmap!.GetPixel(16, 16));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void PixelShader_WorksAsInlineLayerFilterReadingCtxPixelAndSample()
    {
        var source = """
            output = { dimensions = { width = 16, height = 16 } }
            layers = {
              box = {
                type = fill
                color = rgb(200, 50, 100)
                shader = "(u, v, ctx) => [1.0 - ctx.pixel.r, ctx.pixel.g, ctx.sample(u, v).b, ctx.pixel.a]"
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var px = result.Bitmap!.GetPixel(8, 8);
        Assert.Equal(55, px.Red);
        Assert.Equal(50, px.Green);
        Assert.Equal(100, px.Blue);
        Assert.Equal(255, px.Alpha);
    }

    [Fact]
    public void PixelShader_SupportsSkslRuntimeEffectWithUniforms()
    {
        var source = """
            output = { dimensions = { width = 24, height = 24 } }
            layers = {
              sksl_layer = {
                type = shader
                params = {
                  u_tint = rgb(255, 128, 0)
                }
                sksl = "uniform float2 u_resolution; uniform float4 u_tint; half4 main(float2 fragCoord) { float2 uv = fragCoord / u_resolution; return half4(uv.x * u_tint.r, uv.y * u_tint.g, 0.5, 1.0); }"
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Bitmap);
        var topLeft = result.Bitmap!.GetPixel(0, 0);
        var bottomRight = result.Bitmap!.GetPixel(23, 23);
        Assert.NotEqual(topLeft, bottomRight);
    }

    [Fact]
    public void PixelShader_ReportsDiagnosticOnInvalidReturnFormat()
    {
        var source = """
            output = { dimensions = { width = 8, height = 8 } }
            layers = {
              bad = {
                type = shader
                fn = "(u, v) => [1.0, 0.5]"
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == "SHD003");
    }

    [Fact]
    public void PixelShader_ReportsDiagnosticOnInvalidSkslSyntax()
    {
        var source = """
            output = { dimensions = { width = 8, height = 8 } }
            layers = {
              bad_sksl = {
                type = shader
                sksl = "half4 main(float2 p) { return unknown_func(p); }"
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == "SHD004");
    }

    [Fact]
    public void PixelShadersSample_RendersCleanly()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var samplePath = Path.Combine(repoRoot, "samples", "pixel_shaders.gfx");
        var source = File.ReadAllText(samplePath);

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source, masterSeed: 1984, filePath: samplePath);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Bitmap);
        Assert.Equal(192, result.Bitmap!.Width);
        Assert.Equal(192, result.Bitmap!.Height);
    }

    [Fact]
    public void SampleLayerAndSampleCanvas_SamplePixelsAndNormalizedCoordinatesInGfxExpressions()
    {
        const string source = """
            layers = {
              palette_src = {
                type = stripes
                stripe_width = 8
                angle = 0
                color1 = rgb(200, 40, 60)
                color2 = rgb(40, 180, 220)
                visible = false
              }
              bg = {
                type = fill
                color = sample_layer("palette_src", 2, 2)
              }
              top_half = {
                type = rect
                size = { width = 16, height = 8 }
                offset = { x = 0, y = 0 }
                color = sample_layer("palette_src", 0.75, 0.5, true)
              }
              indicator = {
                type = rect
                size = { width = 4, height = 4 }
                offset = { x = 12, y = 12 }
                color = sample_canvas(2, 2)
              }
            }
            output = { dimensions = { width = 16, height = 16 } }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));

        var bmp = result.Bitmap!;
        // Bottom half (from bg, which sampled x=2 of palette_src -> color1 = rgb(200, 40, 60))
        bmp.GetPixel(4, 12).Should().Be(new SKColor(200, 40, 60, 255));
        // Top half (sampled u=0.75 -> x=12 of palette_src -> color2 = rgb(40, 180, 220))
        bmp.GetPixel(4, 2).Should().Be(new SKColor(40, 180, 220, 255));
        // Indicator at (12..15, 12..15) sampled canvas at (2, 2) where top_half had been composited -> color2
        bmp.GetPixel(14, 14).Should().Be(new SKColor(40, 180, 220, 255));
    }

    [Fact]
    public void SampleLayer_ReportsRND056OnUnknownOrCircularLayerReference()
    {
        const string unknownSource = """
            layers = {
              bg = {
                type = fill
                color = sample_layer("missing_layer", 0, 0)
              }
            }
            output = { dimensions = { width = 8, height = 8 } }
            """;

        using var r1 = new GfxCanvasCompositor().RenderSource(unknownSource);
        r1.Diagnostics.Should().Contain(d => d.Code == "RND056" && d.Message.Contains("missing_layer"));

        const string selfRefSource = """
            layers = {
              self_layer = {
                type = fill
                color = sample_layer("self_layer", 0, 0)
              }
            }
            output = { dimensions = { width = 8, height = 8 } }
            """;

        using var r2 = new GfxCanvasCompositor().RenderSource(selfRefSource);
        r2.Diagnostics.Should().Contain(d => d.Code == "RND056" && d.Message.Contains("self_layer"));
    }

    [Fact]
    public void PixelShader_SupportsSampleCanvasInJavaScriptAndSkslShaders()
    {
        const string source = """
            output = { dimensions = { width = 16, height = 16 } }
            layers = {
              bg = {
                type = fill
                color = rgb(100, 150, 200)
              }
              js_invert_top = {
                type = rect
                size = { width = 8, height = 16 }
                offset = { x = 0, y = 0 }
                color = rgb(0, 0, 0)
                shader = "(u, v, ctx) => { const c = ctx.sampleCanvas(u, v); return [1.0 - c.r, 1.0 - c.g, 1.0 - c.b, 1.0]; }"
              }
              sksl_swap_right = {
                type = shader
                size = { width = 8, height = 16 }
                offset = { x = 8, y = 0 }
                sksl = "uniform float2 u_resolution; half4 main(float2 p) { float2 uv = p / u_resolution; half4 c = sampleCanvas(uv); return half4(c.b, c.g, c.r, 1.0); }"
              }
            }
            """;

        using var result = new GfxCanvasCompositor().RenderSource(source);
        result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));

        var bmp = result.Bitmap!;
        // Left half (JS shader inverted bg(100, 150, 200) -> (155, 105, 55))
        bmp.GetPixel(2, 8).Should().Be(new SKColor(155, 105, 55, 255));
        // Right half (SkSL shader swapped R and B of bg(100, 150, 200) -> (200, 150, 100))
        bmp.GetPixel(12, 8).Should().Be(new SKColor(200, 150, 100, 255));
    }
}
