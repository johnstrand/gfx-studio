using FluentAssertions;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Output;
using GfxStudio.Core.Rendering;
using SkiaSharp;

namespace GfxStudio.Tests.Scripting;

public sealed class JsScriptingTests
{
    [Fact]
    public void TopLevelScript_ExportsFunctions_InvokedBareAndWithParameters()
    {
        var tempDir = CreateTemporaryDirectory();
        try
        {
            var jsPath = Path.Combine(tempDir, "logic.js");
            File.WriteAllText(jsPath, """
                export function getLabel(ctx) {
                    return "card_" + ctx.run.number;
                }
                export function getMultiplier(factor, ctx) {
                    return factor * 10;
                }
                export function pickColor(ctx) {
                    return ctx.rgb(255, 128, 0);
                }
                """);

            var gfxPath = Path.Combine(tempDir, "test.gfx");
            File.WriteAllText(gfxPath, """
                script = "logic.js"

                layers = {
                  bg = {
                    type = fill
                    color = pickColor
                  }
                  lbl = {
                    type = text
                    text = getLabel
                    font_size = getMultiplier(2)
                  }
                }

                output = {
                  dimensions = { width = 32, height = 32 }
                  filename = "out.png"
                }
                """);

            var engine = new GfxEngine();
            using var result = engine.RenderFile(gfxPath);

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().ContainSingle();

            // Background should be RGB(255, 128, 0)
            result.Artifacts[0].Bitmap.GetPixel(0, 0).Should().Be(new SKColor(255, 128, 0, 255));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void NamedScripts_ExposeNamespaces_AndMathRandomIsDeterministic()
    {
        var tempDir = CreateTemporaryDirectory();
        try
        {
            var jsPath = Path.Combine(tempDir, "rand.js");
            File.WriteAllText(jsPath, """
                export function nextVal(ctx) {
                    return Math.floor(Math.random() * 100);
                }
                """);

            var gfxPath = Path.Combine(tempDir, "rand.gfx");
            File.WriteAllText(gfxPath, """
                scripts = {
                  gen = "rand.js"
                }

                layers = {
                  l1 = {
                    type = text
                    text = "${gen.nextVal}"
                    font_size = 12
                  }
                }

                output = {
                  mode = series
                  count = 3
                  dimensions = { width = 16, height = 16 }
                  filename = "rand_${run.number}.png"
                }
                """);

            var engine = new GfxEngine();
            using var run1 = engine.RenderFile(gfxPath, new RenderOptions { SeedOverride = 12345UL });
            using var run2 = engine.RenderFile(gfxPath, new RenderOptions { SeedOverride = 12345UL });

            run1.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, run1.Diagnostics));
            run2.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, run2.Diagnostics));

            run1.Artifacts.Should().HaveCount(3);
            for (var i = 0; i < 3; i++)
            {
                run1.Artifacts[i].EncodedBytes.Should().Equal(run2.Artifacts[i].EncodedBytes);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void InfiniteLoopInScript_ReportsDiagnosticError()
    {
        var tempDir = CreateTemporaryDirectory();
        try
        {
            var jsPath = Path.Combine(tempDir, "loop.js");
            File.WriteAllText(jsPath, """
                export function badLoop(ctx) {
                    while (true) {}
                    return 0;
                }
                """);

            var gfxPath = Path.Combine(tempDir, "loop.gfx");
            File.WriteAllText(gfxPath, """
                script = "loop.js"

                layers = {
                  l1 = {
                    type = text
                    text = "${badLoop}"
                  }
                }

                output = {
                  dimensions = { width = 16, height = 16 }
                  filename = "loop.png"
                }
                """);

            var engine = new GfxEngine();
            using var result = engine.RenderFile(gfxPath);

            result.HasErrors.Should().BeTrue();
            result.Diagnostics.Should().Contain(d => d.Code == "JS004" || d.Code == "JS003");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AnimationGif_InvokesJsFunctionPerFrameWithUpdatedContext()
    {
        var tempDir = CreateTemporaryDirectory();
        try
        {
            var jsPath = Path.Combine(tempDir, "anim.js");
            File.WriteAllText(jsPath, """
                export function getFrameColor(ctx) {
                    // Test ctx.frame.number, ctx.frame.progress, ctx.output.dimensions, and hsl
                    const h = ctx.frame.progress * 360;
                    return ctx.hsl(h, 1.0, 0.5);
                }
                export function getFrameTitle(ctx) {
                    return "F" + ctx.frame.number + "@" + ctx.output.dimensions.width;
                }
                """);

            var gfxPath = Path.Combine(tempDir, "anim.gfx");
            File.WriteAllText(gfxPath, """
                script = "anim.js"

                layers = {
                  bg = {
                    type = fill
                    color = getFrameColor
                  }
                  title = {
                    type = text
                    text = getFrameTitle
                    font_size = 14
                  }
                }

                output = {
                  mode = animation_gif
                  frames = 4
                  fps = 10
                  dimensions = { width = 32, height = 32 }
                  filename = "anim.gif"
                }
                """);

            var engine = new GfxEngine();
            using var result = engine.RenderFile(gfxPath);

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().ContainSingle();
            result.Artifacts[0].FilePath.Should().EndWith(".gif");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void JsColorHelpers_HslHexAndColorRamp_EvaluateProperly()
    {
        var tempDir = CreateTemporaryDirectory();
        try
        {
            var jsPath = Path.Combine(tempDir, "palette.js");
            File.WriteAllText(jsPath, """
                export function makeRamp(ctx) {
                    return ctx.color_ramp(
                        ctx.hex("#FF0000"),
                        ctx.hsl(120, 1.0, 0.5),
                        ctx.rgb(0, 0, 255)
                    );
                }
                """);

            var gfxPath = Path.Combine(tempDir, "palette.gfx");
            File.WriteAllText(gfxPath, """
                script = "palette.js"

                layers = {
                  grad = {
                    type = gradient
                    style = "linear"
                    color = makeRamp
                    angle = 90
                  }
                }

                output = {
                  dimensions = { width = 32, height = 32 }
                  filename = "grad.png"
                }
                """);

            var engine = new GfxEngine();
            using var result = engine.RenderFile(gfxPath);

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void JsSyntaxError_ReportsDiagnosticWithLineAndColumn()
    {
        var tempDir = CreateTemporaryDirectory();
        try
        {
            var jsPath = Path.Combine(tempDir, "broken.js");
            File.WriteAllText(jsPath, """
                function invalidSyntax() {
                   return {{{;
                }
                """);

            var gfxPath = Path.Combine(tempDir, "broken.gfx");
            File.WriteAllText(gfxPath, """
                script = "broken.js"

                layers = {
                  bg = {
                    type = fill
                    color = rgb(0, 0, 0)
                  }
                }

                output = {
                  dimensions = { width = 16, height = 16 }
                  filename = "broken.png"
                }
                """);

            var engine = new GfxEngine();
            using var result = engine.RenderFile(gfxPath);

            result.HasErrors.Should().BeTrue();
            result.Diagnostics.Should().Contain(d => d.Code == "JS002");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gfxstudio-js-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
