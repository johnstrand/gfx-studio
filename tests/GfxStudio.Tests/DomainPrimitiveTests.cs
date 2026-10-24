using FluentAssertions;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Tests;

public sealed class DomainPrimitiveTests
{
    [Theory]
    [InlineData(0, 10, "Width")]
    [InlineData(10, 0, "Height")]
    public void Dimensions_RejectsNonPositiveValues(int width, int height, string parameterName)
    {
        var act = () => new Dimensions(width, height);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(parameterName);
    }

    [Fact]
    public void SourceSpan_FormatsFileAndStartLocation()
    {
        var span = new SourceSpan("file.gfx", new SourceLocation(3, 7, 15), new SourceLocation(3, 12, 20));

        span.ToString().Should().Be("file.gfx(3,7)");
    }

    [Fact]
    public void GfxDiagnostic_ContainsFormattedSourceLocation()
    {
        var diagnostic = new GfxDiagnostic(
            "GFX1001",
            "Unexpected token.",
            new SourceSpan("file.gfx", new SourceLocation(3, 7, 15), new SourceLocation(3, 12, 20)),
            DiagnosticSeverity.Error);

        diagnostic.ToString().Should().Be("file.gfx(3,7): error GFX1001: Unexpected token.");
    }

    [Fact]
    public void GfxException_PreservesStructuredDiagnostics()
    {
        var diagnostic = new GfxDiagnostic(
            "GFX1001",
            "Unexpected token.",
            new SourceSpan("file.gfx", new SourceLocation(3, 7, 15), new SourceLocation(3, 12, 20)),
            DiagnosticSeverity.Error);

        var exception = new GfxException(diagnostic);

        exception.Diagnostics.Should().ContainSingle().Which.Should().Be(diagnostic);
        exception.Message.Should().Contain("file.gfx(3,7)");
    }
}
