using FluentAssertions;
using GfxStudio.Core.Randomness;

namespace GfxStudio.Tests.Randomness;

public sealed class DeterministicSeedProviderTests
{
    [Fact]
    public void SameExecutionTuple_ProducesSameSeedAndRandomSequence()
    {
        var provider = new DeterministicSeedProvider(0x0123456789ABCDEFUL);
        var first = provider.CreateRandom(3, 2, "layer0");
        var second = provider.CreateRandom(3, 2, "layer0");

        provider.DeriveSeed(3, 2, "layer0").Should().Be(provider.DeriveSeed(3, 2, "layer0"));
        Enumerable.Range(0, 1_000).Select(_ => first.NextUInt64())
            .Should().Equal(Enumerable.Range(0, 1_000).Select(_ => second.NextUInt64()));
    }

    [Fact]
    public void DifferentExecutionTuples_ProduceDistinctAvalanchedSeeds()
    {
        var provider = new DeterministicSeedProvider(987654321UL);
        var baseline = provider.DeriveSeed(5, 7, "layer2");
        var variations = new[]
        {
            provider.DeriveSeed(6, 7, "layer2"),
            provider.DeriveSeed(5, 8, "layer2"),
            provider.DeriveSeed(5, 7, "layer3"),
            provider.DeriveSeed(5, 7, "Layer2"),
            provider.DeriveSeed(0, 0, "layer2")
        };

        variations.Should().OnlyHaveUniqueItems();
        variations.Should().OnlyContain(seed => seed != baseline);
        variations.Select(seed => System.Numerics.BitOperations.PopCount(seed ^ baseline))
            .Should().OnlyContain(distance => distance >= 20 && distance <= 44);
    }

    [Fact]
    public void LayerStreams_AreIndependentOfCreationOrder()
    {
        var provider = new DeterministicSeedProvider(123UL);
        var layerOneFirst = provider.CreateRandom(5, 0, "layer1");
        var layerTwo = provider.CreateRandom(5, 0, "layer2");
        for (var index = 0; index < 500; index++)
        {
            layerTwo.NextUInt64();
        }

        var layerOneAfter = provider.CreateRandom(5, 0, "layer1");

        Enumerable.Range(0, 1_000).Select(_ => layerOneFirst.NextUInt64())
            .Should().Equal(Enumerable.Range(0, 1_000).Select(_ => layerOneAfter.NextUInt64()));
    }
}
