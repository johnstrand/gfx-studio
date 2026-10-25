using FluentAssertions;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Tests.Evaluation;

public sealed class DimensionArithmeticTests
{
    [Fact]
    public void DimensionScalarArithmetic_EvaluatesCorrectly()
    {
        const string source = """
            output = {
              dimensions = { width = 100, height = 200 }
            }

            divided = output.dimensions / 2
            multiplied = output.dimensions * 0.5
            multiplied_reverse = 2 * output.dimensions
            added = output.dimensions + 10
            subtracted = output.dimensions - 10
            modulo = output.dimensions % 30
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var root = result.Value!;

        root.Properties["divided"].Should().Be(new GfxDimensions(new Dimensions(50, 100)));
        root.Properties["multiplied"].Should().Be(new GfxDimensions(new Dimensions(50, 100)));
        root.Properties["multiplied_reverse"].Should().Be(new GfxDimensions(new Dimensions(200, 400)));
        root.Properties["added"].Should().Be(new GfxDimensions(new Dimensions(110, 210)));
        root.Properties["subtracted"].Should().Be(new GfxDimensions(new Dimensions(90, 190)));
        root.Properties["modulo"].Should().Be(new GfxDimensions(new Dimensions(10, 20)));
    }

    [Fact]
    public void DimensionComponentWiseArithmetic_EvaluatesCorrectly()
    {
        const string source = """
            d1 = { width = 100, height = 200 }
            d2 = { width = 30, height = 70 }

            added = d1 + d2
            subtracted = d1 - d2
            multiplied = d1 * d2
            divided = d1 / d2
            modulo = d1 % d2
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var root = result.Value!;

        root.Properties["added"].Should().Be(new GfxDimensions(new Dimensions(130, 270)));
        root.Properties["subtracted"].Should().Be(new GfxDimensions(new Dimensions(70, 130)));
        root.Properties["multiplied"].Should().Be(new GfxDimensions(new Dimensions(3000, 14000)));
        root.Properties["divided"].Should().Be(new GfxDimensions(new Dimensions(3, 3))); // 100/30 ~ 3.33 -> 3, 200/70 ~ 2.86 -> 3
        root.Properties["modulo"].Should().Be(new GfxDimensions(new Dimensions(10, 60)));
    }

    [Fact]
    public void DimensionArithmetic_DivisionOrModuloByZero_EmitsEVAL007()
    {
        const string source = """
            d = { width = 100, height = 200 }
            div_zero = d / 0
            mod_zero = d % 0
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Where(d => d.Code == "EVAL007").Should().HaveCount(2);
    }

    [Fact]
    public void DimensionArithmetic_NonPositiveResult_EmitsEVAL010()
    {
        const string source = """
            d = { width = 100, height = 200 }
            sub_too_much = d - 150
            zero_mod = d % 50
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Where(d => d.Code == "EVAL010").Should().HaveCount(2);
    }

    [Fact]
    public void PointArithmetic_ScalarAndComponentWise_EvaluatesCorrectly()
    {
        const string source = """
            p1 = { x = 10, y = 20 }
            p2 = { x = 5, y = -5 }
            d = { width = 15, height = 25 }

            scaled = p1 * 2
            scaled_reverse = 2 * p1
            offset_scalar = p1 + 5
            div_scalar = p1 / 2
            mod_scalar = p1 % 3
            added_points = p1 + p2
            point_plus_dims = p1 + d
            dims_plus_point = d + p1
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var root = result.Value!;

        var scaled = root.Properties["scaled"].Should().BeOfType<GfxObject>().Subject;
        scaled.Properties["x"].Should().Be(new GfxNumber(20));
        scaled.Properties["y"].Should().Be(new GfxNumber(40));

        var scaledRev = root.Properties["scaled_reverse"].Should().BeOfType<GfxObject>().Subject;
        scaledRev.Properties["x"].Should().Be(new GfxNumber(20));
        scaledRev.Properties["y"].Should().Be(new GfxNumber(40));

        var offsetScalar = root.Properties["offset_scalar"].Should().BeOfType<GfxObject>().Subject;
        offsetScalar.Properties["x"].Should().Be(new GfxNumber(15));
        offsetScalar.Properties["y"].Should().Be(new GfxNumber(25));

        var divScalar = root.Properties["div_scalar"].Should().BeOfType<GfxObject>().Subject;
        divScalar.Properties["x"].Should().Be(new GfxNumber(5));
        divScalar.Properties["y"].Should().Be(new GfxNumber(10));

        var modScalar = root.Properties["mod_scalar"].Should().BeOfType<GfxObject>().Subject;
        modScalar.Properties["x"].Should().Be(new GfxNumber(1));
        modScalar.Properties["y"].Should().Be(new GfxNumber(2));

        var addedPoints = root.Properties["added_points"].Should().BeOfType<GfxObject>().Subject;
        addedPoints.Properties["x"].Should().Be(new GfxNumber(15));
        addedPoints.Properties["y"].Should().Be(new GfxNumber(15));

        var ptPlusDims = root.Properties["point_plus_dims"].Should().BeOfType<GfxObject>().Subject;
        ptPlusDims.Properties["x"].Should().Be(new GfxNumber(25));
        ptPlusDims.Properties["y"].Should().Be(new GfxNumber(45));

        var dimsPlusPt = root.Properties["dims_plus_point"].Should().BeOfType<GfxObject>().Subject;
        dimsPlusPt.Properties["x"].Should().Be(new GfxNumber(25));
        dimsPlusPt.Properties["y"].Should().Be(new GfxNumber(45));
    }

    [Fact]
    public void PointArithmetic_DivisionByZero_EmitsEVAL007()
    {
        const string source = """
            p = { x = 10, y = 20 }
            div_zero = p / 0
            mod_zero = p % 0
            comp_zero = p / { x = 0, y = 5 }
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Where(d => d.Code == "EVAL007").Should().HaveCount(3);
    }

    [Fact]
    public void GfxDimensions_CoercesToPoint2D()
    {
        const string source = """
            output = {
              dimensions = { width = 640, height = 480 }
            }

            center = output.dimensions / 2
            offset = output.dimensions / 4
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var root = result.Value!;

        var center = root.Properties["center"];
        center.TryCoerceToPoint2D(out var centerPt).Should().BeTrue();
        centerPt.Should().Be(new Point2D(320, 240));

        var offset = root.Properties["offset"];
        offset.TryCoerceToPoint2D(out var offsetPt).Should().BeTrue();
        offsetPt.Should().Be(new Point2D(160, 120));
    }

    [Fact]
    public void GfxDimensionsAndPoints_SupportsMemberAccess()
    {
        const string source = """
            dims = { width = 300, height = 150 }
            w = dims.width
            h = dims.height
            pt = { x = 40, y = 80 }
            px = pt.x
            py = pt.y
            """;

        var result = GfxEvaluator.EvaluateSource(source);

        result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var root = result.Value!;

        root.Properties["w"].Should().Be(new GfxNumber(300));
        root.Properties["h"].Should().Be(new GfxNumber(150));
        root.Properties["px"].Should().Be(new GfxNumber(40));
        root.Properties["py"].Should().Be(new GfxNumber(80));
    }

    [Fact]
    public void LayerProperties_CanUseDimensionsArithmeticForCenterAndOffset()
    {
        const string source = """
            layers = {
              circle = {
                type = circle
                center = output.dimensions / 2
                radius = 20
                fill_color = rgb(255, 0, 0)
              }
              rect = {
                type = rect
                offset = output.dimensions / 4
                size = output.dimensions / 4
                fill_color = rgb(0, 255, 0)
              }
            }

            output = {
              dimensions = { width = 128, height = 128 }
            }
            """;

        using var result = new GfxStudio.Core.Rendering.GfxCanvasCompositor().RenderSource(source);

        result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        result.Bitmap.Should().NotBeNull();
        result.Bitmap!.Width.Should().Be(128);
        result.Bitmap!.Height.Should().Be(128);
    }
}
