using FluentAssertions;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Tests;

public sealed class ColorRampTests
{
    private static readonly ColorRamp BlackToWhite = new(
    [
        new ColorStop(0f, ColorRgba.Black),
        new ColorStop(1f, ColorRgba.White)
    ]);

    [Theory]
    [InlineData(0f, 0, 0, 0)]
    [InlineData(0.5f, 128, 128, 128)]
    [InlineData(1f, 255, 255, 255)]
    public void Sample_InterpolatesBetweenStops(float t, byte red, byte green, byte blue)
    {
        BlackToWhite.Sample(t).Should().Be(new ColorRgba(red, green, blue));
    }

    [Theory]
    [InlineData(-1f, 0, 0, 0)]
    [InlineData(2f, 255, 255, 255)]
    public void Sample_ClampsOutsideUnitInterval(float t, byte red, byte green, byte blue)
    {
        BlackToWhite.Sample(t).Should().Be(new ColorRgba(red, green, blue));
    }

    [Fact]
    public void Sample_InterpolatesAcrossMultipleStops()
    {
        var ramp = new ColorRamp(
        [
            new ColorStop(0f, new ColorRgba(0, 0, 0)),
            new ColorStop(0.25f, new ColorRgba(100, 50, 0)),
            new ColorStop(1f, new ColorRgba(0, 150, 255))
        ]);

        ramp.Sample(0.625f).Should().Be(new ColorRgba(50, 100, 128));
    }

    [Fact]
    public void SampleSmooth_UsesSmoothstepInterpolation()
    {
        BlackToWhite.SampleSmooth(0.5f).Should().Be(new ColorRgba(128, 128, 128));
        BlackToWhite.SampleSmooth(0.25f).Should().Be(new ColorRgba(40, 40, 40));
    }

    [Fact]
    public void Constructor_RequiresAtLeastTwoStops()
    {
        var act = () => new ColorRamp([new ColorStop(0f, ColorRgba.Black)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RequiresIncreasingStopPositions()
    {
        var act = () => new ColorRamp(
        [
            new ColorStop(0.5f, ColorRgba.Black),
            new ColorStop(0.5f, ColorRgba.White)
        ]);

        act.Should().Throw<ArgumentException>();
    }
}
