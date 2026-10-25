using FluentAssertions;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Output;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Rendering;
using Xunit;

namespace GfxStudio.Tests.Diagnostics;

public sealed class PropertyValidationTests
{
    [Fact]
    public void SuggestionHelper_ComputesDistanceAndFindsClosestMatch()
    {
        PropertySuggestionHelper.LevenshteinDistance("colr", "color").Should().Be(1);
        PropertySuggestionHelper.LevenshteinDistance("widht", "width").Should().Be(2);
        PropertySuggestionHelper.LevenshteinDistance("dimentions", "dimensions").Should().Be(1);

        var candidates = new[] { "color", "dimensions", "width", "height", "opacity" };
        PropertySuggestionHelper.FindBestMatch("colr", candidates).Should().Be("color");
        PropertySuggestionHelper.FindBestMatch("widht", candidates).Should().Be("width");
        PropertySuggestionHelper.FindBestMatch("completely_unknown", candidates).Should().BeNull();

        PropertySuggestionHelper.GetSuggestionSuffix("colr", candidates).Should().Be(" Did you mean 'color'?");
        PropertySuggestionHelper.GetSuggestionSuffix("completely_unknown", candidates).Should().BeEmpty();
    }

    [Fact]
    public void OutputConfigBinder_WarnsOnUnknownPropertyWithSuggestion()
    {
        const string source = """
            output = {
              mode = single
              dimentions = { width = 32, height = 32 }
              dimensions = { width = 32, height = 32 }
              filename = "test.png"
              extra_unknown_param = 123
            }
            """;

        var parseResult = GfxParser.Parse(source);
        parseResult.HasErrors.Should().BeFalse();

        var binder = new OutputConfigBinder();
        var bindResult = binder.Bind(parseResult.Document!);

        bindResult.Config.Should().NotBeNull();
        bindResult.Diagnostics.Should().Contain(d =>
            d.Code == "OUT022"
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message.Contains("Unknown property 'dimentions' in 'output' block. Did you mean 'dimensions'?"));

        bindResult.Diagnostics.Should().Contain(d =>
            d.Code == "OUT022"
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message == "Unknown property 'extra_unknown_param' in 'output' block.");
    }

    [Fact]
    public void Compositor_WarnsOnUnknownLayerPropertyWithSuggestionAndSourceSpan()
    {
        const string source = """
            output = {
              dimensions = { width = 16, height = 16 }
              filename = "test.png"
            }

            layers = {
              bg = {
                type = fill
                colr = rgb(255, 0, 0)
                color = rgb(255, 0, 0)
                completely_invalid = 42
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap.Should().NotBeNull();

        var typoDiag = result.Diagnostics.FirstOrDefault(d =>
            d.Code == "RND050" && d.Message.Contains("'colr'"));

        typoDiag.Should().NotBeNull();
        typoDiag!.Severity.Should().Be(DiagnosticSeverity.Warning);
        typoDiag.Message.Should().Be("Unknown property 'colr' on layer 'bg' of type 'fill'. Did you mean 'color'?");
        typoDiag.Span.Start.Line.Should().Be(9);

        var unknownDiag = result.Diagnostics.FirstOrDefault(d =>
            d.Code == "RND050" && d.Message.Contains("'completely_invalid'"));

        unknownDiag.Should().NotBeNull();
        unknownDiag!.Severity.Should().Be(DiagnosticSeverity.Warning);
        unknownDiag.Message.Should().Be("Unknown property 'completely_invalid' on layer 'bg' of type 'fill'.");
    }

    [Fact]
    public void Compositor_WarnsOnUnknownInlineEffectProperty()
    {
        const string source = """
            output = {
              dimensions = { width = 16, height = 16 }
              filename = "test.png"
            }

            layers = {
              main = {
                type = fill
                color = rgb(0, 255, 0)
                blur = {
                  radios = 4
                  radius = 4
                  extra_effect_prop = true
                }
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap.Should().NotBeNull();

        result.Diagnostics.Should().Contain(d =>
            d.Code == "FX009"
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message == "Unknown property 'radios' on effect 'blur' on layer 'main'. Did you mean 'radius'?");

        result.Diagnostics.Should().Contain(d =>
            d.Code == "FX009"
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message == "Unknown property 'extra_effect_prop' on effect 'blur' on layer 'main'.");
    }

    [Fact]
    public void Compositor_WarnsOnUnknownPropertyInEffectsArray()
    {
        const string source = """
            output = {
              dimensions = { width = 16, height = 16 }
              filename = "test.png"
            }

            layers = {
              main = {
                type = fill
                color = rgb(0, 0, 255)
                effects = [
                  {
                    type = blur
                    radios = 2
                    radius = 2
                  }
                ]
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap.Should().NotBeNull();

        result.Diagnostics.Should().Contain(d =>
            d.Code == "FX009"
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message == "Unknown property 'radios' on effect 'blur' on layer 'main'. Did you mean 'radius'?");
    }

    [Fact]
    public void CustomTopLevelVariables_DoNotTriggerWarnings()
    {
        const string source = """
            tile_size = 16
            primary_color = rgb(255, 0, 0)
            theme_name = "retro"

            output = {
              dimensions = { width = tile_size, height = tile_size }
              filename = "test.png"
            }

            layers = {
              base = {
                type = fill
                color = primary_color
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();
        result.Bitmap.Should().NotBeNull();
    }

    [Fact]
    public void AdjustmentLayer_WarnsOnUnknownPropertyWithSuggestion()
    {
        const string source = """
            output = {
              dimensions = { width = 16, height = 16 }
              filename = "test.png"
            }

            layers = {
              bg = {
                type = fill
                color = rgb(255, 255, 255)
              }
              adj = {
                type = vignette
                intencity = 0.5
                intensity = 0.5
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Bitmap.Should().NotBeNull();

        result.Diagnostics.Should().Contain(d =>
            d.Code == "RND050"
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message == "Unknown property 'intencity' on layer 'adj' of type 'vignette'. Did you mean 'intensity'?");
    }

    [Fact]
    public void UniversalProperties_DoNotTriggerWarningsOnAnyLayer()
    {
        const string source = """
            output = {
              dimensions = { width = 16, height = 16 }
              filename = "test.png"
            }

            layers = {
              bg = {
                type = fill
                color = rgb(100, 100, 100)
                size = { width = 16, height = 16 }
                offset = { x = 0, y = 0 }
                position = { x = 0, y = 0 }
                mix = normal
                opacity = 0.8
                visible = true
                enabled = true
                rotation = 0
                scale = 1.0
                transform_scale = 1.0
                flip_x = false
                flip_y = false
                anchor = { x = 0.5, y = 0.5 }
                pivot = { x = 0.5, y = 0.5 }
                resample = nearest
              }
            }
            """;

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();
        result.Bitmap.Should().NotBeNull();
    }
}
