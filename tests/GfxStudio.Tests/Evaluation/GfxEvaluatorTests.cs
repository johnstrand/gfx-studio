using FluentAssertions;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Tests.Evaluation;

public sealed class GfxEvaluatorTests
{
    [Fact]
    public void EvaluateSource_ResolvesForwardCrossReferencesAndCoercesDimensions()
    {
        const string source = """
            layers = {
              layer1 = {
                type = fractal_clouds
                mix = multiply
                size = output.dimensions
                double_width = output.dimensions.width * 2
              }
            }

            output = {
              dimensions = { width = 64, height = 64 }
              filename = "worn_number_${run.number:0000}.png"
            }
            """;

        var result = GfxEvaluator.EvaluateSource(source, new RunContext(Number: 7, Count: 10, Seed: 42));

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();

        result.Value.Should().NotBeNull();
        var root = result.Value!;
        var layers = root.Properties["layers"].Should().BeOfType<GfxObject>().Subject;
        var layer1 = layers.Properties["layer1"].Should().BeOfType<GfxObject>().Subject;

        // Unquoted identifiers in value positions become GfxSymbol
        layer1.Properties["type"].Should().Be(new GfxSymbol("fractal_clouds"));
        layer1.Properties["mix"].Should().Be(new GfxSymbol("multiply"));
        layer1.Properties["mix"].TryCoerceToBlendMode(out var blendMode).Should().BeTrue();
        blendMode.Should().Be(BlendMode.Multiply);

        // Forward reference to output.dimensions resolves to GfxDimensions(64, 64)
        layer1.Properties["size"].Should().Be(new GfxDimensions(new Dimensions(64, 64)));
        layer1.Properties["double_width"].Should().Be(new GfxNumber(128));

        // Interpolated string with :0000 format specifier and run.number == 7
        var output = root.Properties["output"].Should().BeOfType<GfxObject>().Subject;
        output.Properties["dimensions"].Should().Be(new GfxDimensions(new Dimensions(64, 64)));
        output.Properties["filename"].Should().Be(new GfxString("worn_number_0007.png"));
    }

    [Fact]
    public void EvaluateSource_DetectsCircularReferencesAndReportsCyclePath()
    {
        const string source = """
            a = b
            b = c
            c = a
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d =>
            d.Code == "EVAL001" &&
            d.Message.Contains("a -> b -> c -> a", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateSource_ResolvesNamedColorsAlignAndMathConstants()
    {
        const string source = """
            white = NAMED_COLORS.WHITE
            black = NAMED_COLORS.BLACK
            transparent = NAMED_COLORS.TRANSPARENT
            align = { vertical: ALIGN.CENTER, horizontal: ALIGN.RIGHT }
            offset = { x = 12.5, y = -4 }
            circle_circumference = 2 * PI * 10
            full_turn = TAU
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.Diagnostics.Should().BeEmpty();
        var root = result.Value!;

        root.Properties["white"].Should().Be(new GfxColor(new ColorRgba(255, 255, 255, 255)));
        root.Properties["black"].Should().Be(new GfxColor(new ColorRgba(0, 0, 0, 255)));
        root.Properties["transparent"].Should().Be(new GfxColor(new ColorRgba(0, 0, 0, 0)));

        root.Properties["align"].TryCoerceToAlignment(out var alignment).Should().BeTrue();
        alignment.Should().Be(new Alignment(HorizontalAlign.Right, VerticalAlign.Center));

        root.Properties["offset"].TryCoerceToPoint2D(out var point).Should().BeTrue();
        point.Should().Be(new Point2D(12.5f, -4f));

        root.Properties["circle_circumference"].Should().BeOfType<GfxNumber>()
            .Which.Value.Should().BeApproximately(2 * Math.PI * 10, 1e-9);
        root.Properties["full_turn"].Should().BeOfType<GfxNumber>()
            .Which.Value.Should().BeApproximately(Math.Tau, 1e-9);
    }

    [Fact]
    public void EvaluateSource_ExposesRunAndFrameContextVariablesAndShorthandT()
    {
        const string source = """
            run_num = run.number
            run_cnt = run.count
            run_sd = run.seed
            frame_num = frame.number
            frame_cnt = frame.count
            frame_tm = frame.time
            frame_prog = frame.progress
            shorthand_t = t
            """;

        var run = new RunContext(Number: 3, Count: 10, Seed: 999);
        var frame = new FrameContext(Number: 5, Count: 20, Fps: 10.0);

        var result = GfxEvaluator.EvaluateSource(source, run, frame);

        result.Diagnostics.Should().BeEmpty();
        var root = result.Value!;

        root.Properties["run_num"].Should().Be(new GfxNumber(3));
        root.Properties["run_cnt"].Should().Be(new GfxNumber(10));
        root.Properties["run_sd"].Should().Be(new GfxNumber(999));
        root.Properties["frame_num"].Should().Be(new GfxNumber(5));
        root.Properties["frame_cnt"].Should().Be(new GfxNumber(20));
        root.Properties["frame_tm"].Should().Be(new GfxNumber(0.5));
        root.Properties["frame_prog"].Should().Be(new GfxNumber(0.25));
        root.Properties["shorthand_t"].Should().Be(new GfxNumber(0.25));
    }

    [Fact]
    public void EvaluateSource_InvokesRegisteredFunctionsAndAutoInvokesBareFunctionRefs()
    {
        const string source = """
            explicit_call = double_val(21)
            bare_ref = current_run_label
            """;

        var functions = new IGfxCallable[]
        {
            new DelegateCallable("double_val", (args, _, _) =>
                new GfxNumber(((GfxNumber)args[0]).Value * 2)),
            new DelegateCallable("current_run_label", (_, ctx, _) =>
                new GfxString($"Run #{ctx.Run.Number}"))
        };

        var result = GfxEvaluator.EvaluateSource(source, new RunContext(Number: 4, Count: 5, Seed: 1), functions: functions);

        result.Diagnostics.Should().BeEmpty();
        result.Value!.Properties["explicit_call"].Should().Be(new GfxNumber(42));
        result.Value!.Properties["bare_ref"].Should().Be(new GfxString("Run #4"));
    }

    [Fact]
    public void EvaluateSource_ReportsDiagnosticsForUndefinedNamespacesMembersAndDivisionByZero()
    {
        const string source = """
            bad_ns = UNKNOWN_NS.WHITE
            bad_member = NAMED_COLORS.NOT_A_COLOR
            div_zero = 10 / 0
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Select(d => d.Code).Should().Contain(["EVAL002", "EVAL003", "EVAL007"]);
    }

    private sealed class DelegateCallable(
        string name,
        Func<IReadOnlyList<GfxValue>, EvaluationContext, SourceSpan, GfxValue> handler) : IGfxCallable
    {
        public string Name { get; } = name;

        public GfxValue Invoke(IReadOnlyList<GfxValue> arguments, EvaluationContext context, SourceSpan callSpan) =>
            handler(arguments, context, callSpan);
    }
}
