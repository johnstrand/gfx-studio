using GfxStudio.Core.Randomness.Seeding;

namespace GfxStudio.Core.Randomness;

/// <summary>Derives independent deterministic random streams for each run, frame, and layer.</summary>
public sealed class DeterministicSeedProvider
{
    public DeterministicSeedProvider(ulong masterSeed)
    {
        MasterSeed = masterSeed;
    }

    public ulong MasterSeed { get; }

    /// <summary>Derives a stable 64-bit seed for the specified execution scope.</summary>
    public ulong DeriveSeed(int runNumber, int frameNumber, string layerKey)
    {
        ArgumentNullException.ThrowIfNull(layerKey);

        var hash = SeedMixer.Mix(MasterSeed ^ 0x243F6A8885A308D3UL);
        hash = SeedMixer.Mix(hash ^ unchecked((ulong)(long)runNumber) ^ 0x13198A2E03707344UL);
        hash = SeedMixer.Mix(hash ^ unchecked((ulong)(long)frameNumber) ^ 0xA4093822299F31D0UL);
        hash = SeedMixer.Mix(hash ^ SeedMixer.HashString(layerKey) ^ 0x082EFA98EC4E6C89UL);
        return hash;
    }

    /// <summary>Creates the deterministic random stream for the specified layer execution.</summary>
    public GfxRandom CreateRandom(int runNumber, int frameNumber, string layerKey) =>
        new(DeriveSeed(runNumber, frameNumber, layerKey));
}
