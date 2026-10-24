using FluentAssertions;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Parsing.Ast;

namespace GfxStudio.Tests.Parsing;

public sealed class GfxParserTests
{
    [Fact]
    public void Parse_NumberGfx_ProducesCompleteLosslessAstAndPreservesLayerOrder()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "number.gfx"));

        var result = GfxParser.Parse(source, "number.gfx");

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();
        result.Document.Should().NotBeNull();

        var doc = result.Document!;
        doc.Properties.Select(p => p.Name).Should().Equal("layers", "output");

        // Verify layers block and exact layer declaration order
        var layersBlock = doc.Properties[0].Value.Should().BeOfType<ObjectBlockExpr>().Subject;
        layersBlock.Properties.Select(p => p.Name).Should().Equal("layer0", "layer1", "layer2");

        // layer0
        var layer0 = layersBlock.Properties[0].Value.Should().BeOfType<ObjectBlockExpr>().Subject;
        layer0.Properties.Select(p => p.Name).Should().Equal("type", "color");
        layer0.Properties[0].Value.Should().BeOfType<IdentifierExpr>().Which.Name.Should().Be("fill");

        var rgbCall = layer0.Properties[1].Value.Should().BeOfType<FunctionCallExpr>().Subject;
        rgbCall.FunctionName.Should().Be("rgb");
        rgbCall.Arguments.Select(a => ((NumberLiteralExpr)a).Value).Should().Equal(0d, 255d, 255d);

        // layer1
        var layer1 = layersBlock.Properties[1].Value.Should().BeOfType<ObjectBlockExpr>().Subject;
        layer1.Properties.Select(p => p.Name).Should().Equal("type", "color", "mix", "size");
        layer1.Properties[0].Value.Should().BeOfType<IdentifierExpr>().Which.Name.Should().Be("fractal_clouds");

        var rampCall = layer1.Properties[1].Value.Should().BeOfType<FunctionCallExpr>().Subject;
        rampCall.FunctionName.Should().Be("color_ramp");
        rampCall.Arguments.Should().HaveCount(2);
        var whiteRef = rampCall.Arguments[0].Should().BeOfType<MemberAccessExpr>().Subject;
        whiteRef.Target.Should().BeOfType<IdentifierExpr>().Which.Name.Should().Be("NAMED_COLORS");
        whiteRef.MemberName.Should().Be("WHITE");

        layer1.Properties[2].Value.Should().BeOfType<IdentifierExpr>().Which.Name.Should().Be("multiply");
        var sizeRef = layer1.Properties[3].Value.Should().BeOfType<MemberAccessExpr>().Subject;
        sizeRef.Target.Should().BeOfType<IdentifierExpr>().Which.Name.Should().Be("output");
        sizeRef.MemberName.Should().Be("dimensions");

        // layer2
        var layer2 = layersBlock.Properties[2].Value.Should().BeOfType<ObjectBlockExpr>().Subject;
        layer2.Properties.Select(p => p.Name).Should().Equal("type", "size", "text", "font_size", "align");

        var floorCall = layer2.Properties[2].Value.Should().BeOfType<FunctionCallExpr>().Subject;
        floorCall.FunctionName.Should().Be("floor");
        var randomCall = floorCall.Arguments.Single().Should().BeOfType<FunctionCallExpr>().Subject;
        randomCall.FunctionName.Should().Be("random");
        randomCall.Arguments.Select(a => ((NumberLiteralExpr)a).Value).Should().Equal(0d, 10d);

        var fontSize = layer2.Properties[3].Value.Should().BeOfType<NumberLiteralExpr>().Subject;
        fontSize.Value.Should().Be(48d);
        fontSize.IsInteger.Should().BeTrue();

        var alignBlock = layer2.Properties[4].Value.Should().BeOfType<ObjectBlockExpr>().Subject;
        alignBlock.Properties.Select(p => p.Name).Should().Equal("vertical", "horizontal");
        alignBlock.Properties[0].Value.Should().BeOfType<MemberAccessExpr>().Which.MemberName.Should().Be("CENTER");
        alignBlock.Properties[1].Value.Should().BeOfType<MemberAccessExpr>().Which.MemberName.Should().Be("CENTER");

        // output block
        var outputBlock = doc.Properties[1].Value.Should().BeOfType<ObjectBlockExpr>().Subject;
        outputBlock.Properties.Select(p => p.Name).Should().Equal("dimensions", "filename");

        var dimsBlock = outputBlock.Properties[0].Value.Should().BeOfType<ObjectBlockExpr>().Subject;
        dimsBlock.Properties.Select(p => p.Name).Should().Equal("width", "height");
        dimsBlock.Properties[0].Value.Should().BeOfType<NumberLiteralExpr>().Which.Value.Should().Be(64d);
        dimsBlock.Properties[1].Value.Should().BeOfType<NumberLiteralExpr>().Which.Value.Should().Be(64d);

        var filenameExpr = outputBlock.Properties[1].Value.Should().BeOfType<InterpolatedStringExpr>().Subject;
        filenameExpr.Parts.Should().HaveCount(3);
        filenameExpr.Parts[0].Should().BeOfType<InterpolatedTextPart>().Which.Text.Should().Be("worn_number_");

        var interpPart = filenameExpr.Parts[1].Should().BeOfType<InterpolatedExpressionPart>().Subject;
        interpPart.Format.Should().Be("0000");
        var runNumberExpr = interpPart.Expression.Should().BeOfType<MemberAccessExpr>().Subject;
        runNumberExpr.Target.Should().BeOfType<IdentifierExpr>().Which.Name.Should().Be("run");
        runNumberExpr.MemberName.Should().Be("number");

        filenameExpr.Parts[2].Should().BeOfType<InterpolatedTextPart>().Which.Text.Should().Be(".png");
    }

    [Fact]
    public void Parse_ArithmeticExpressions_RespectsPrattPrecedenceAndAssociativity()
    {
        const string source = """
            a = 64 * 2 + 10
            b = 10 + 64 * 2
            c = (10 + 64) * 2
            d = 20 - 5 - 3
            e = -5 + +3 * 2 % 4 / 2
            """;

        var result = GfxParser.Parse(source);

        result.Diagnostics.Should().BeEmpty();
        var props = result.Document!.Properties;

        // a = (64 * 2) + 10
        var aExpr = props[0].Value.Should().BeOfType<BinaryExpr>().Subject;
        aExpr.Operator.Should().Be(BinaryOperator.Add);
        aExpr.Left.Should().BeOfType<BinaryExpr>().Which.Operator.Should().Be(BinaryOperator.Multiply);
        aExpr.Right.Should().BeOfType<NumberLiteralExpr>().Which.Value.Should().Be(10d);

        // b = 10 + (64 * 2)
        var bExpr = props[1].Value.Should().BeOfType<BinaryExpr>().Subject;
        bExpr.Operator.Should().Be(BinaryOperator.Add);
        bExpr.Left.Should().BeOfType<NumberLiteralExpr>().Which.Value.Should().Be(10d);
        bExpr.Right.Should().BeOfType<BinaryExpr>().Which.Operator.Should().Be(BinaryOperator.Multiply);

        // c = (10 + 64) * 2
        var cExpr = props[2].Value.Should().BeOfType<BinaryExpr>().Subject;
        cExpr.Operator.Should().Be(BinaryOperator.Multiply);
        cExpr.Left.Should().BeOfType<BinaryExpr>().Which.Operator.Should().Be(BinaryOperator.Add);
        cExpr.Right.Should().BeOfType<NumberLiteralExpr>().Which.Value.Should().Be(2d);

        // d = (20 - 5) - 3 (left-associative)
        var dExpr = props[3].Value.Should().BeOfType<BinaryExpr>().Subject;
        dExpr.Operator.Should().Be(BinaryOperator.Subtract);
        var dLeft = dExpr.Left.Should().BeOfType<BinaryExpr>().Subject;
        dLeft.Operator.Should().Be(BinaryOperator.Subtract);
        dLeft.Left.Should().BeOfType<NumberLiteralExpr>().Which.Value.Should().Be(20d);
        dLeft.Right.Should().BeOfType<NumberLiteralExpr>().Which.Value.Should().Be(5d);
        dExpr.Right.Should().BeOfType<NumberLiteralExpr>().Which.Value.Should().Be(3d);

        // e = (-5) + (((+3 * 2) % 4) / 2)
        var eExpr = props[4].Value.Should().BeOfType<BinaryExpr>().Subject;
        eExpr.Operator.Should().Be(BinaryOperator.Add);
        eExpr.Left.Should().BeOfType<UnaryExpr>().Which.Operator.Should().Be(UnaryOperator.Minus);
        var divExpr = eExpr.Right.Should().BeOfType<BinaryExpr>().Subject;
        divExpr.Operator.Should().Be(BinaryOperator.Divide);
        var modExpr = divExpr.Left.Should().BeOfType<BinaryExpr>().Subject;
        modExpr.Operator.Should().Be(BinaryOperator.Modulo);
        var mulExpr = modExpr.Left.Should().BeOfType<BinaryExpr>().Subject;
        mulExpr.Operator.Should().Be(BinaryOperator.Multiply);
        mulExpr.Left.Should().BeOfType<UnaryExpr>().Which.Operator.Should().Be(UnaryOperator.Plus);
    }

    [Fact]
    public void Parse_SupportsArraysBooleansStringsAndNamespacedFunctionCalls()
    {
        const string source = """
            script = "helpers.js"
            items = [ 1, 2.5, "three", true, False, { x: 10, y: 20 }, ]
            value = scriptAlias.computeValue(10, run.number)
            """;

        var result = GfxParser.Parse(source);

        result.Diagnostics.Should().BeEmpty();
        var props = result.Document!.Properties;

        props[0].Value.Should().BeOfType<StringLiteralExpr>().Which.Value.Should().Be("helpers.js");

        var array = props[1].Value.Should().BeOfType<ArrayExpr>().Subject;
        array.Elements.Should().HaveCount(6);
        array.Elements[0].Should().BeOfType<NumberLiteralExpr>().Which.IsInteger.Should().BeTrue();
        array.Elements[1].Should().BeOfType<NumberLiteralExpr>().Which.IsInteger.Should().BeFalse();
        array.Elements[2].Should().BeOfType<StringLiteralExpr>().Which.Value.Should().Be("three");
        array.Elements[3].Should().BeOfType<BooleanLiteralExpr>().Which.Value.Should().BeTrue();
        array.Elements[4].Should().BeOfType<BooleanLiteralExpr>().Which.Value.Should().BeFalse();
        array.Elements[5].Should().BeOfType<ObjectBlockExpr>();

        var call = props[2].Value.Should().BeOfType<FunctionCallExpr>().Subject;
        call.FunctionName.Should().Be("computeValue");
        var callee = call.Callee.Should().BeOfType<MemberAccessExpr>().Subject;
        callee.Target.Should().BeOfType<IdentifierExpr>().Which.Name.Should().Be("scriptAlias");
        callee.MemberName.Should().Be("computeValue");
        call.Arguments.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_MalformedInputs_ReportsDiagnosticsWithSpansAndRecoversInPanicMode()
    {
        const string source = """
            valid_before = 10
            missing_assign 42
            bad_expr = * 5
            unclosed_call = rgb(255, 0
            valid_after = 99
            unclosed_block = {
              x = 1
            """;

        var result = GfxParser.Parse(source, "broken.gfx");

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().HaveCountGreaterThanOrEqualTo(4);

        // Verify panic-mode synchronization preserved valid properties before and after errors
        result.Document!.Properties.Select(p => p.Name).Should().Contain(["valid_before", "valid_after", "unclosed_block"]);

        // Check line numbers of diagnostics
        result.Diagnostics.First(d => d.Code == "PAR003").Span.Start.Line.Should().Be(2); // missing '=' or ':'
        result.Diagnostics.First(d => d.Code == "PAR009").Span.Start.Line.Should().Be(3); // unexpected '*'
        result.Diagnostics.First(d => d.Code == "PAR011").Span.Start.Line.Should().Be(4); // unclosed '('
        result.Diagnostics.First(d => d.Code == "PAR005").Span.Start.Line.Should().Be(6); // unclosed '{'
    }
}
