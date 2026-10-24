using FluentAssertions;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Functions;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Tests.Functions;

public sealed class StandardFunctionsTests
{
    [Fact]
    public void DefaultRegistry_RegistersAllStandardFunctions()
    {
        var registry = GfxFunctionRegistry.CreateDefault();
        string[] names =
        [
            "rgb", "rgba", "hsl", "hsla", "hex", "color_ramp",
            "random", "random_int", "random_choice",
            "floor", "ceil", "round", "abs", "sign", "min", "max", "clamp",
            "sqrt", "pow", "exp", "log", "sin", "cos", "tan", "asin", "acos", "atan2",
            "deg_to_rad", "rad_to_deg", "lerp", "smoothstep", "ease_in", "ease_out", "ease_in_out", "ping_pong"
        ];

        foreach (var name in names)
        {
            registry.TryGetFunction(name, out _).Should().BeTrue(name);
            registry.Functions[name].Should().BeAssignableTo<IGfxFunction>();
        }
    }

    [Fact]
    public void EvaluateSource_EvaluatesColorMathAndEasingFunctions()
    {
        const string source = """
            rgb_value = rgb(300, -20, 12.4)
            rgba_value = rgba(1, 2, 3, 0.5)
            hsl_value = hsl(0, 1, 0.5)
            hsla_value = hsla(120, 1, 0.5, 0.25)
            hex_rgb = hex("#12a0FF")
            hex_rgba = hex("#12a0FF80")
            ramp_value = color_ramp(NAMED_COLORS.WHITE, NAMED_COLORS.BLACK, NAMED_COLORS.RED)
            stop_ramp = color_ramp({ pos = 0, color = NAMED_COLORS.WHITE }, { pos = 1, color = NAMED_COLORS.BLACK })
            floor_value = floor(-1.2)
            ceil_value = ceil(-1.2)
            round_value = round(1.5)
            abs_value = abs(-4)
            sign_value = sign(-4)
            min_value = min(3, -4, 2)
            max_value = max(3, -4, 2)
            clamp_value = clamp(9, -2, 5)
            sqrt_value = sqrt(9)
            pow_value = pow(2, 3)
            exp_value = exp(0)
            log_value = log(E)
            sin_value = sin(PI / 2)
            cos_value = cos(0)
            tan_value = tan(0)
            asin_value = asin(1)
            acos_value = acos(1)
            atan2_value = atan2(1, 0)
            deg_value = deg_to_rad(180)
            rad_value = rad_to_deg(PI)
            lerp_number = lerp(2, 10, 0.25)
            lerp_color = lerp(rgb(0, 0, 0), rgb(255, 255, 255), 0.5)
            smooth_value = smoothstep(0, 1, 0.5)
            ease_in_value = ease_in(0.5)
            ease_out_value = ease_out(0.5)
            ease_in_out_value = ease_in_out(0.5)
            ping_pong_value = ping_pong(0.25)
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.Diagnostics.Should().BeEmpty();
        var values = result.Value!.Properties;
        values["rgb_value"].Should().Be(new GfxColor(new ColorRgba(255, 0, 12)));
        values["rgba_value"].Should().Be(new GfxColor(new ColorRgba(1, 2, 3, 128)));
        values["hsl_value"].Should().Be(new GfxColor(new ColorRgba(255, 0, 0)));
        values["hsla_value"].Should().Be(new GfxColor(new ColorRgba(0, 255, 0, 64)));
        values["hex_rgb"].Should().Be(new GfxColor(new ColorRgba(0x12, 0xA0, 0xFF)));
        values["hex_rgba"].Should().Be(new GfxColor(new ColorRgba(0x12, 0xA0, 0xFF, 0x80)));

        var ramp = values["ramp_value"].Should().BeOfType<GfxColorRamp>().Subject.Ramp;
        ramp.Stops.Should().HaveCount(3);
        ramp.Stops.Select(stop => stop.Position).Should().Equal(0f, 0.5f, 1f);
        ramp.Sample(0.5f).Should().Be(ColorRgba.Black);
        values["stop_ramp"].Should().BeOfType<GfxColorRamp>().Which.Ramp.Stops
            .Select(stop => stop.Position).Should().Equal(0f, 1f);

        AssertNumber(values, "floor_value", -2);
        AssertNumber(values, "ceil_value", -1);
        AssertNumber(values, "round_value", 2);
        AssertNumber(values, "abs_value", 4);
        AssertNumber(values, "sign_value", -1);
        AssertNumber(values, "min_value", -4);
        AssertNumber(values, "max_value", 3);
        AssertNumber(values, "clamp_value", 5);
        AssertNumber(values, "sqrt_value", 3);
        AssertNumber(values, "pow_value", 8);
        AssertNumber(values, "exp_value", 1);
        AssertNumber(values, "log_value", 1);
        AssertNumber(values, "sin_value", 1, 1e-12);
        AssertNumber(values, "cos_value", 1);
        AssertNumber(values, "tan_value", 0);
        AssertNumber(values, "asin_value", Math.PI / 2, 1e-12);
        AssertNumber(values, "acos_value", 0);
        AssertNumber(values, "atan2_value", Math.PI / 2, 1e-12);
        AssertNumber(values, "deg_value", Math.PI, 1e-12);
        AssertNumber(values, "rad_value", 180, 1e-12);
        AssertNumber(values, "lerp_number", 4);
        values["lerp_color"].Should().Be(new GfxColor(new ColorRgba(128, 128, 128)));
        AssertNumber(values, "smooth_value", 0.5);
        AssertNumber(values, "ease_in_value", 0.25);
        AssertNumber(values, "ease_out_value", 0.75);
        AssertNumber(values, "ease_in_out_value", 0.5);
        AssertNumber(values, "ping_pong_value", 0.5);
    }

    [Fact]
    public void EvaluateSource_EvaluatesNumberGfxWithDeterministicRandomFunctions()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "number.gfx"));
        var run = new RunContext(Number: 5, Count: 100, Seed: 0xDEADBEEF);

        var first = GfxEvaluator.EvaluateSource(source, run);
        var second = GfxEvaluator.EvaluateSource(source, run);

        first.Diagnostics.Should().BeEmpty();
        second.Diagnostics.Should().BeEmpty();
        var firstRoot = first.Value!;
        var layers = firstRoot.Properties["layers"].Should().BeOfType<GfxObject>().Subject;
        layers.Properties["layer0"].Should().BeOfType<GfxObject>().Which.Properties["color"]
            .Should().Be(new GfxColor(new ColorRgba(0, 255, 255)));
        layers.Properties["layer1"].Should().BeOfType<GfxObject>().Which.Properties["color"]
            .Should().BeOfType<GfxColorRamp>();
        var firstText = layers.Properties["layer2"].Should().BeOfType<GfxObject>().Subject.Properties["text"]
            .Should().BeOfType<GfxNumber>().Subject.Value;
        var secondLayers = second.Value!.Properties["layers"].Should().BeOfType<GfxObject>().Subject;
        var secondText = secondLayers.Properties["layer2"].Should().BeOfType<GfxObject>().Subject.Properties["text"]
            .Should().BeOfType<GfxNumber>().Subject.Value;

        firstText.Should().Be(secondText);
        firstText.Should().BeInRange(0, 9);
        Math.Truncate(firstText).Should().Be(firstText);
    }

    [Fact]
    public void RandomFunctions_ReturnValuesWithinTheirSpecifiedRanges()
    {
        const string source = """
            unit = random()
            ranged = random(-2, 3)
            integer = random_int(2, 5)
            choice = random_choice([10, 20, 30])
            """;

        for (var seed = 0; seed < 100; seed++)
        {
            var result = GfxEvaluator.EvaluateSource(source, new RunContext(0, 1, (ulong)seed));
            result.Diagnostics.Should().BeEmpty();
            var values = result.Value!.Properties;
            values["unit"].Should().BeOfType<GfxNumber>().Which.Value.Should().BeInRange(0, Math.BitDecrement(1.0));
            values["ranged"].Should().BeOfType<GfxNumber>().Which.Value.Should().BeInRange(-2, Math.BitDecrement(3.0));
            values["integer"].Should().BeOfType<GfxNumber>().Which.Value.Should().BeInRange(2, 4);
            new[] { 10.0, 20.0, 30.0 }.Should().Contain(values["choice"].Should().BeOfType<GfxNumber>().Subject.Value);
        }
    }

    [Fact]
    public void EvaluateSource_ReportsArityAndTypeErrorsAtTheFunctionCallSpan()
    {
        const string source = "bad = rgb(1, 2)\nwrong = floor(\"x\")";

        var result = GfxEvaluator.EvaluateSource(source, filePath: "functions.gfx");

        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal("FUNC001", "FUNC002");
        result.Diagnostics[0].Span.Should().Be(new SourceSpan(
            "functions.gfx",
            new SourceLocation(1, 7, 6),
            new SourceLocation(1, 16, 15)));
        result.Diagnostics[1].Span.Should().Be(new SourceSpan(
            "functions.gfx",
            new SourceLocation(2, 9, 24),
            new SourceLocation(2, 19, 34)));
    }

    [Fact]
    public void EvaluateSource_ReportsInvalidColorAndRandomArgumentsAsDiagnostics()
    {
        const string source = "bad_hex = hex(\"nope\")\nbad_random = random(1)\nbad_choice = random_choice([])";

        var result = GfxEvaluator.EvaluateSource(source);

        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal("FUNC003", "FUNC001", "FUNC003");
        result.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Span.Start.Line >= 1 && diagnostic.Span.Start.Line <= 3);
    }

    private static void AssertNumber(IReadOnlyDictionary<string, GfxValue> values, string key, double expected, double tolerance = 0.0)
    {
        var value = values[key].Should().BeOfType<GfxNumber>().Subject.Value;
        if (tolerance == 0.0)
        {
            value.Should().Be(expected);
        }
        else
        {
            value.Should().BeApproximately(expected, tolerance);
        }
    }
}
