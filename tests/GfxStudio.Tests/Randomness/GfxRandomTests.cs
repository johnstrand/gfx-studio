using FluentAssertions;
using GfxStudio.Core.Randomness;

namespace GfxStudio.Tests.Randomness;

public sealed class GfxRandomTests
{
    [Fact]
    public void SplitMix64_ProducesKnownSequence()
    {
        var random = new SplitMix64(0);

        random.NextUInt64().Should().Be(0xE220A8397B1DCDAFUL);
        random.NextUInt64().Should().Be(0x6E789E6AA1B965F4UL);
        random.NextUInt64().Should().Be(0x06C45D188009454FUL);
    }

    [Fact]
    public void SameSeed_ProducesIdenticalSequencesAcrossTenThousandSamples()
    {
        var first = new GfxRandom(123456789UL);
        var second = new GfxRandom(123456789UL);

        for (var index = 0; index < 10_000; index++)
        {
            first.NextUInt64().Should().Be(second.NextUInt64());
        }
    }

    [Fact]
    public void NextUInt64_MatchesXoshiro256StarStarReferenceVector()
    {
        var random = new GfxRandom(0UL);

        random.NextUInt64().Should().Be(0x99EC5F36CB75F2B4UL);
    }

    [Fact]
    public void NextDoubleAndNextFloat_StayInUnitInterval()
    {
        var random = new GfxRandom(42UL);

        for (var index = 0; index < 10_000; index++)
        {
            random.NextDouble().Should().BeInRange(0.0, Math.BitDecrement(1.0));
            random.NextFloat().Should().BeInRange(0.0f, MathF.BitDecrement(1.0f));
        }
    }

    [Fact]
    public void NextIntAndNextDouble_RangeOverloadsStayWithinBoundsAndAreReasonablyUniform()
    {
        var random = new GfxRandom(987654321UL);
        var integerBuckets = new int[10];
        var doubleBuckets = new int[10];

        for (var index = 0; index < 100_000; index++)
        {
            var integer = random.NextInt(-5, 5);
            integer.Should().BeInRange(-5, 4);
            integerBuckets[integer + 5]++;

            var value = random.NextDouble(-2.0, 3.0);
            (value >= -2.0 && value < 3.0).Should().BeTrue();
            var bucket = Math.Clamp((int)((value + 2.0) * 2.0), 0, doubleBuckets.Length - 1);
            doubleBuckets[bucket]++;
        }

        integerBuckets.Should().OnlyContain(count => count > 9_000 && count < 11_000);
        doubleBuckets.Should().OnlyContain(count => count > 9_000 && count < 11_000);
    }

    [Fact]
    public void NextInt_SupportsTheEntireSignedIntegerRange()
    {
        var random = new GfxRandom(55UL);

        for (var index = 0; index < 100; index++)
        {
            random.NextInt(int.MinValue, int.MaxValue).Should().BeInRange(int.MinValue, int.MaxValue - 1);
        }
    }

    [Fact]
    public void RangeMethods_RejectInvalidBounds()
    {
        var random = new GfxRandom(1UL);

        var intAction = () => random.NextInt(1, 1);
        var doubleAction = () => random.NextDouble(2.0, 1.0);
        intAction.Should().Throw<ArgumentOutOfRangeException>();
        doubleAction.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextDouble_RangeSupportsFiniteEndpointsWithOverflowingDifference()
    {
        var random = new GfxRandom(87UL);

        for (var index = 0; index < 100; index++)
        {
            var value = random.NextDouble(-double.MaxValue, double.MaxValue);
            double.IsFinite(value).Should().BeTrue();
            (value >= -double.MaxValue && value < double.MaxValue).Should().BeTrue();
        }
    }

    [Fact]
    public void NextGaussian_UsesRequestedMeanAndStandardDeviation()
    {
        var random = new GfxRandom(7654321UL);
        const int sampleCount = 50_000;
        var values = new double[sampleCount];

        for (var index = 0; index < sampleCount; index++)
        {
            values[index] = random.NextGaussian(3.0, 2.0);
        }

        var average = values.Average();
        var variance = values.Average(value => Math.Pow(value - average, 2));
        average.Should().BeApproximately(3.0, 0.04);
        Math.Sqrt(variance).Should().BeApproximately(2.0, 0.06);
        random.NextGaussian(7.0, 0.0).Should().Be(7.0);
    }

    [Fact]
    public void Fork_IsStableScopedAndDoesNotAdvanceParent()
    {
        var firstParent = new GfxRandom(321UL);
        var secondParent = new GfxRandom(321UL);
        var firstChild = firstParent.Fork("layer0");
        var repeatedChild = firstParent.Fork("layer0");
        var otherChild = firstParent.Fork("layer1");

        firstParent.NextUInt64().Should().Be(secondParent.NextUInt64());
        Enumerable.Range(0, 100).Select(_ => firstChild.NextUInt64())
            .Should().Equal(Enumerable.Range(0, 100).Select(_ => repeatedChild.NextUInt64()));
        firstParent.Fork("layer0").NextUInt64().Should().NotBe(otherChild.NextUInt64());
    }
}
