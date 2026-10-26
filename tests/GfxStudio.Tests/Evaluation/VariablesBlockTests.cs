using FluentAssertions;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Tests.Evaluation;

public sealed class VariablesBlockTests
{
    [Fact]
    public void Variables_CanBeReferencedBareAndQualified()
    {
        const string source = """
            variables = {
              base_size = 64
              primary_color = hex("#ff5500")
            }

            layers = {
              bg = {
                type = fill
                color = primary_color
                alt_color = variables.primary_color
                size = base_size
                alt_size = variables.base_size
              }
            }

            output = {
              dimensions = { width = base_size, height = variables.base_size }
              filename = "out.png"
            }
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeFalse();
        result.Value.Should().NotBeNull();

        var layers = result.Value!.Properties["layers"] as GfxObject;
        layers.Should().NotBeNull();
        var bg = layers!.Properties["bg"] as GfxObject;
        bg.Should().NotBeNull();

        bg!.Properties["size"].Should().Be(new GfxNumber(64));
        bg.Properties["alt_size"].Should().Be(new GfxNumber(64));
        bg.Properties["color"].Should().Be(new GfxColor(new ColorRgba(255, 85, 0, 255)));
        bg.Properties["alt_color"].Should().Be(new GfxColor(new ColorRgba(255, 85, 0, 255)));

        var output = result.Value.Properties["output"] as GfxObject;
        output!.Properties["dimensions"].TryCoerceToDimensions(out var dims).Should().BeTrue();
        dims.Width.Should().Be(64);
        dims.Height.Should().Be(64);
    }

    [Fact]
    public void Variables_CanCrossReferenceSiblingVariables()
    {
        const string source = """
            variables = {
              base = 10
              multiplier = 4
              width = base * multiplier
              height = width * 2
            }

            output = {
              dimensions = { width = width, height = height }
              filename = "test.png"
            }
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeFalse();
        result.Value.Should().NotBeNull();

        var variables = result.Value!.Properties["variables"] as GfxObject;
        variables.Should().NotBeNull();
        variables!.Properties["base"].Should().Be(new GfxNumber(10));
        variables.Properties["multiplier"].Should().Be(new GfxNumber(4));
        variables.Properties["width"].Should().Be(new GfxNumber(40));
        variables.Properties["height"].Should().Be(new GfxNumber(80));

        var output = result.Value.Properties["output"] as GfxObject;
        output!.Properties["dimensions"].TryCoerceToDimensions(out var dims).Should().BeTrue();
        dims.Width.Should().Be(40);
        dims.Height.Should().Be(80);
    }

    [Fact]
    public void Variables_CircularReferenceEmitsEVAL001()
    {
        const string source = """
            variables = {
              a = b + 1
              b = a + 1
            }

            output = {
              dimensions = { width = a, height = b }
              filename = "test.png"
            }
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "EVAL001" && d.Message.Contains("Circular property reference"));
    }

    [Fact]
    public void Variables_DirectSelfReferenceEmitsEVAL001()
    {
        const string source = """
            variables = {
              a = a + 1
            }

            output = {
              dimensions = { width = a, height = 10 }
              filename = "test.png"
            }
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "EVAL001" && d.Message.Contains("Circular property reference"));
    }

    [Fact]
    public void Variables_QualifiedCircularReferenceEmitsEVAL001()
    {
        const string source = """
            variables = {
              a = variables.b + 1
              b = variables.a + 1
            }

            output = {
              dimensions = { width = 10, height = 10 }
              filename = "test.png"
            }
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "EVAL001" && d.Message.Contains("Circular property reference"));
    }

    [Fact]
    public void Variables_NonObjectEmitsEVAL011()
    {
        const string source = """
            variables = 123

            output = {
              dimensions = { width = 10, height = 10 }
              filename = "test.png"
            }
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "EVAL011" && d.Message.Contains("Top-level 'variables' property must be an object block."));
    }

    [Fact]
    public void Variables_ContextOverrides_TakePrecedenceOverDocumentValues()
    {
        const string source = """
            variables = {
              size = 64
              col = hex("#000000")
            }

            layers = {
              bg = {
                type = fill
                color = col
                alt_color = variables.col
                size = size
                alt_size = variables.size
              }
            }

            output = {
              dimensions = { width = size, height = variables.size }
              filename = "out.png"
            }
            """;

        var overrides = new Dictionary<string, GfxValue>
        {
            ["size"] = new GfxNumber(128),
            ["col"] = new GfxColor(new ColorRgba(255, 0, 0, 255))
        };

        var result = GfxEvaluator.EvaluateSource(source, variables: overrides);

        result.HasErrors.Should().BeFalse();
        result.Value.Should().NotBeNull();

        var layers = result.Value!.Properties["layers"] as GfxObject;
        var bg = layers!.Properties["bg"] as GfxObject;

        bg!.Properties["size"].Should().Be(new GfxNumber(128));
        bg.Properties["alt_size"].Should().Be(new GfxNumber(128));
        bg.Properties["color"].Should().Be(new GfxColor(new ColorRgba(255, 0, 0, 255)));
        bg.Properties["alt_color"].Should().Be(new GfxColor(new ColorRgba(255, 0, 0, 255)));

        var output = result.Value.Properties["output"] as GfxObject;
        output!.Properties["dimensions"].TryCoerceToDimensions(out var dims).Should().BeTrue();
        dims.Width.Should().Be(128);
        dims.Height.Should().Be(128);
    }

    [Fact]
    public void Variables_ContextOverrides_SynthesizesVariablesObjectWhenNotDefinedInAst()
    {
        const string source = """
            output = {
              dimensions = { width = size, height = variables.size }
              filename = "out.png"
            }
            """;

        var overrides = new Dictionary<string, GfxValue>
        {
            ["size"] = new GfxNumber(256)
        };

        var result = GfxEvaluator.EvaluateSource(source, variables: overrides);

        result.HasErrors.Should().BeFalse();
        result.Value.Should().NotBeNull();

        var output = result.Value!.Properties["output"] as GfxObject;
        output!.Properties["dimensions"].TryCoerceToDimensions(out var dims).Should().BeTrue();
        dims.Width.Should().Be(256);
        dims.Height.Should().Be(256);

        var variables = result.Value.Properties["variables"] as GfxObject;
        variables.Should().NotBeNull();
        variables!.Properties["size"].Should().Be(new GfxNumber(256));
    }
}
