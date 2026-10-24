namespace GfxStudio.Core.Output;

public sealed record ImageMapMetadata(
    string Image,
    AtlasDimensions AtlasDimensions,
    AtlasDimensions TileDimensions,
    ulong MasterSeed,
    IReadOnlyList<ImageMapSpriteMetadata> Sprites);

public sealed record AtlasDimensions(int Width, int Height);

public sealed record ImageMapSpriteMetadata(int Index, string Name, int X, int Y, int Width, int Height, ulong Seed);
