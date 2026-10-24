using FluentAssertions;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Tests;

public sealed class ColorRgbaTests
{
    [Fact]
    public void ToSKColor_PreservesEveryChannel()
    {
        var color = new ColorRgba(12, 34, 56, 78);

        color.ToSKColor().Should().Be(new SKColor(12, 34, 56, 78));
    }

    [Fact]
    public void FromSKColor_PreservesEveryChannel()
    {
        ColorRgba.FromSKColor(new SKColor(12, 34, 56, 78))
            .Should().Be(new ColorRgba(12, 34, 56, 78));
    }

    [Fact]
    public void NormalizedChannels_AreInRangeZeroToOne()
    {
        var color = new ColorRgba(0, 128, 255, 64);

        color.NormalizedR.Should().Be(0f);
        color.NormalizedG.Should().BeApproximately(128f / 255f, 0.0001f);
        color.NormalizedB.Should().Be(1f);
        color.NormalizedA.Should().BeApproximately(64f / 255f, 0.0001f);
    }
}
