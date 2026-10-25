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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gfxstudio-js-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
